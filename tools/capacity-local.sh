#!/usr/bin/env bash
# Called by replica-stack --capacity; k6 itself also runs against remote fixtures.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
: "${FIXTURE:?}" "${FLEET_LOG_DIR:?}"
export RATE=${RATE:-1000} ROUTES=${ROUTES:-sites}
run_seconds=${CAPACITY_SECONDS:-60}
export SUMMARY="$FLEET_LOG_DIR/summary.json"
k6=${K6:-k6}
observers=()
cleanup() {
  for pid in "${observers[@]:-}"; do [ -n "$pid" ] && kill -TERM "$pid" 2>/dev/null || true; done
}
trap cleanup EXIT
sql() { docker exec fleet-pg psql -X -U postgres -d locintel -tA -c "$1"; }
python3 - "$FLEET_LOG_DIR" "$run_seconds" <<'PY'
import hashlib, json, os, platform, subprocess, sys
from pathlib import Path
keys=['RATE','ROUTES','VUS','AUTH','P95_MS','P99_MS','FLEET_REPLICAS','FLEET_WORKERS','FLEET_PG_CPUS','FLEET_POOL_SIZE','FLEET_POOLER','Traffic__MaxConcurrentRequests','SEED_SITES','FLEET_AUTO_PREPARE','WARMUP_RATE','WARMUP_SECONDS']
manifest={k:os.environ.get(k) for k in keys}
manifest.update(seconds=int(sys.argv[2]), platform=platform.platform(), sha=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(), dirty=subprocess.check_output(['git','status','--short'],text=True), topology='local shared host; not cross-host capacity')
manifest['source_sha256'] = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path('src/LocIntel.Api/HttpPolicyHosting.cs'), Path('src/LocIntel.Api/GatewayIdentityCache.cs'), Path('src/LocIntel.Api/Program.cs'), *Path('tools').glob('capacity*'), Path('tools/replica-stack.sh'), Path('tools/fleet-bench.mjs')] if p.is_file()}
Path(sys.argv[1],'manifest.json').write_text(json.dumps(manifest,indent=2))
PY
sql "SELECT count(*) AS sites FROM tenancy.sites; SELECT count(*) AS schedules FROM tenancy.site_schedules;" > "$FLEET_LOG_DIR/dataset.txt"
"$k6" version > "$FLEET_LOG_DIR/k6-version.txt"
# Warmup has its own output and cannot contribute successes to the measurement.
RATE="${WARMUP_RATE:-50}" CAPACITY_SECONDS="${WARMUP_SECONDS:-10}" EXPECT_BALANCE=false SUMMARY="$FLEET_LOG_DIR/warmup.json" "$k6" run --quiet "$root/tools/capacity.k6.js" > "$FLEET_LOG_DIR/warmup.log" 2>&1 || true
sql 'SELECT pg_stat_statements_reset();' >/dev/null
if command -v "${DOTNET_COUNTERS:-dotnet-counters}" >/dev/null; then
  for pid in ${FLEET_API_IDS//,/ }; do
    "${DOTNET_COUNTERS:-dotnet-counters}" collect -p "$pid" --counters 'EventCounters\System.Runtime,Npgsql,Microsoft.AspNetCore.Hosting' --refresh-interval 2 --maxHistograms 100 --format csv -o "$FLEET_LOG_DIR/runtime-$pid.csv" --duration "$(((run_seconds + 15) / 3600)):$((((run_seconds + 15) / 60) % 60)):$(((run_seconds + 15) % 60))" > "$FLEET_LOG_DIR/counters-$pid.log" 2>&1 &
    observers+=($!)
  done
fi
(
  while true; do
    date -u '+%Y-%m-%dT%H:%M:%SZ'
    observed_pids="$FLEET_PROCESS_IDS"
    [ ! -f "$FLEET_LOG_DIR/load.pid" ] || observed_pids="$observed_pids,$(cat "$FLEET_LOG_DIR/load.pid")"
    ps -p "$observed_pids" -o pid=,%cpu=,rss=,time=,comm=
    sql "SELECT state, wait_event_type, wait_event, count(*) FROM pg_stat_activity WHERE usename='app_user' GROUP BY 1,2,3; SELECT status,count(*) FROM wolverine.wolverine_incoming_envelopes GROUP BY 1; SELECT count(*) AS dead_letters FROM wolverine.wolverine_dead_letters; SELECT wal_bytes FROM pg_stat_wal;"
    docker stats --no-stream --format '{{.Name}} cpu={{.CPUPerc}} memory={{.MemUsage}} io={{.BlockIO}}' fleet-pg
    sleep 3
  done
) > "$FLEET_LOG_DIR/resources.log" 2>&1 &
observers+=($!)
set +e
CAPACITY_SECONDS="$run_seconds" "$k6" run --quiet --out "json=$FLEET_LOG_DIR/samples.json.gz" "$root/tools/capacity.k6.js" > "$FLEET_LOG_DIR/load.log" 2>&1 &
load_pid=$!
echo "$load_pid" > "$FLEET_LOG_DIR/load.pid"
wait "$load_pid"
result=$?
set -e
sql "SELECT calls,total_exec_time,rows,shared_blks_hit,shared_blks_read,wal_bytes,query FROM pg_stat_statements ORDER BY total_exec_time DESC LIMIT 25;" > "$FLEET_LOG_DIR/statements.txt"
python3 "$root/tools/capacity-report.py" "$FLEET_LOG_DIR"
# A successful HTTP run is not acceptable if its quota path failed open.
if rg -q 'rate counter unreachable|connection pool has been exhausted' "$FLEET_LOG_DIR"/api-*.log; then result=1; fi
tail -n 3 "$FLEET_LOG_DIR/load.log"
echo "Capacity evidence: $FLEET_LOG_DIR (k6 exit $result)"
exit "$result"
