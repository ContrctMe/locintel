#!/usr/bin/env bash
set -euo pipefail
# Protected throughput probe against the isolated gateway reference.
# Configure a sufficient operational allowance for the fixture tenant first.
# Usage: K6=/path/to/k6 tools/gateway-capacity.sh <API replicas>
count=${1:?API replica count}
case "$count" in 1|2|4) ;; *) echo "choose 1, 2, or 4 API replicas" >&2; exit 1;; esac
command -v "${K6:-k6}" >/dev/null
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
run_dir="$root/coverage/gateway/capacity-${count}-$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$run_dir"
compose=(tools/gateway-stack.sh compose)
"${compose[@]}" up -d --no-deps --scale "api=$count" api > "$run_dir/setup.log" 2>&1
address=$("${compose[@]}" port --index 1 gateway 8080)
ready=false
for attempt in $(seq 1 15); do
  if FLEET_BENCH_FIXTURE="$root/coverage/gateway/fixture.local.json" node tools/fleet-bench.mjs "http://$address" 15 32 "$count" > "$run_dir/fixture.log" 2>&1; then ready=true; break; fi
  sleep 2
done
[ "$ready" = true ] || exit 1
export FIXTURE="$root/coverage/gateway/fixture.local.json"
export RATE=${RATE:-1000} CAPACITY_SECONDS=${CAPACITY_SECONDS:-60} VUS=${VUS:-256} ROUTES=${ROUTES:-sites}
export WARMUP_RATE=${WARMUP_RATE:-50} WARMUP_SECONDS=${WARMUP_SECONDS:-10}
export BASE_URLS=$("${compose[@]}" ps --format json | python3 -c 'import json,sys; rows=[r for line in sys.stdin for r in ([json.loads(line)] if isinstance(json.loads(line),dict) else json.loads(line))]; print(",".join("http://127.0.0.1:"+str(p["PublishedPort"]) for r in rows if r["Service"]=="gateway" for p in r["Publishers"] if p["TargetPort"]==8080))')
"${compose[@]}" ps --format '{{.Name}} {{.Image}} {{.Status}}' > "$run_dir/containers.txt"
"${compose[@]}" exec -T postgres psql -X -U postgres -d locintel -tA -c 'SELECT org_id,count(*) AS sites FROM tenancy.sites GROUP BY org_id;' > "$run_dir/dataset.txt"
docker info --format 'cpus={{.NCPU}} memory_bytes={{.MemTotal}}' > "$run_dir/docker-resources.txt"
for container in $("${compose[@]}" ps -q api); do docker inspect -f '{{.Name}} {{.Image}}' "$container"; done > "$run_dir/api-images.txt"
python3 - "$run_dir" "$count" <<'PY'
import hashlib,json,os,pathlib,platform,subprocess,sys
p=pathlib.Path(sys.argv[1]); f=json.load(open(os.environ['FIXTURE']))
s={'topology':'single host; container services share Docker resources; native k6', 'api_replicas':int(sys.argv[2]),'platform':platform.platform(), 'instances':f['instances'], 'settings':{k:os.getenv(k) for k in ['RATE','CAPACITY_SECONDS','VUS','ROUTES','BASE_URLS','WARMUP_RATE','WARMUP_SECONDS','TRAFFIC_MAX_CONCURRENT_REQUESTS']},'sha':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()}
s['source_sha256']={str(x):hashlib.sha256(x.read_bytes()).hexdigest() for x in [pathlib.Path('deploy/gateway/compose.yaml'),pathlib.Path('deploy/gateway/envoy.yaml'),pathlib.Path('src/LocIntel.Api/HttpPolicyHosting.cs'),pathlib.Path('src/LocIntel.Api/GatewayIdentityCache.cs'),pathlib.Path('tools/capacity.k6.js')]}
p.joinpath('manifest.json').write_text(json.dumps(s,indent=2))
p.joinpath('policy.json').write_text(pathlib.Path('coverage/gateway/runtime/ratelimit/config/policy.yaml').read_text())
PY
k6=${K6:-k6}
RATE="$WARMUP_RATE" CAPACITY_SECONDS="$WARMUP_SECONDS" EXPECT_BALANCE=false SUMMARY="$run_dir/warmup.json" "$k6" run --quiet tools/capacity.k6.js > "$run_dir/warmup.log" 2>&1 || true
"${compose[@]}" exec -T postgres psql -X -U postgres -d locintel -tA -c 'CREATE EXTENSION IF NOT EXISTS pg_stat_statements; SELECT pg_stat_statements_reset();' > /dev/null
gateway_stats() {
  local phase=$1 container
  for container in $("${compose[@]}" ps -q gateway); do
    # Reuse the installed Redis image's wget in the gateway's network namespace;
    # the admin port stays bound to loopback and unpublished.
    docker run --rm --network "container:$container" redis:8.2-alpine@sha256:30abb90e62f14b737010746def3ba99cc79fe19dcdb3d37b41f21fc62e7da19d wget -q -O - 'http://127.0.0.1:9901/stats?format=json' > "$run_dir/gateway-$phase-$container.json"
  done
}
gateway_stats before
(
  while true; do
    date -u '+%Y-%m-%dT%H:%M:%SZ'
    [ ! -f "$run_dir/load.pid" ] || ps -p "$(cat "$run_dir/load.pid")" -o pid=,%cpu=,rss=,time=,comm=
    "${compose[@]}" stats --no-stream --format '{{.Name}} cpu={{.CPUPerc}} memory={{.MemUsage}} io={{.BlockIO}}'
    "${compose[@]}" exec -T postgres psql -X -U postgres -d locintel -tA -c "SELECT state,wait_event_type,wait_event,count(*) FROM pg_stat_activity WHERE usename='app_user' GROUP BY 1,2,3; SELECT status,count(*) FROM wolverine.wolverine_incoming_envelopes GROUP BY 1; SELECT count(*) FROM wolverine.wolverine_dead_letters; SELECT wal_bytes FROM pg_stat_wal;"
    sleep 3
  done
) > "$run_dir/resources.log" 2>&1 &
observer=$!
trap 'kill "$observer" 2>/dev/null || true' EXIT
set +e
SUMMARY="$run_dir/summary.json" "$k6" run --quiet --out "json=$run_dir/samples.json.gz" tools/capacity.k6.js > "$run_dir/load.log" 2>&1 &
load_pid=$!
echo "$load_pid" > "$run_dir/load.pid"
wait "$load_pid"
run_exit=$?
set -e
gateway_stats after
"${compose[@]}" exec -T postgres psql -X -U postgres -d locintel -tA -c 'SELECT calls,total_exec_time,rows,wal_bytes,query FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 25;' > "$run_dir/statements.txt"
python3 tools/capacity-report.py "$run_dir" || run_exit=1
printf '%s\n' "$run_exit" > "$run_dir/exit-code.txt"
tail -2 "$run_dir/load.log"
echo "$run_dir"
exit "$run_exit"
