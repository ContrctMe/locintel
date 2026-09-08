#!/usr/bin/env bash
# The fleet: one Postgres, the migrate role once, then N api and N worker
# replicas from the same build behind a round-robin proxy - the production
# topology (docs/production.md) on one host. Runs the fleet suite against it
# (tests/LocIntel.FleetTests: replica spread and idempotency across
# replicas, a replica killed mid-batch, one sweep per period across workers),
# or the load baseline for the scaling table. Tears everything down.
#
#   tools/replica-stack.sh [replicas]                 # fleet suite (default 2)
#   tools/replica-stack.sh 2 --reporting              # dedicated 100-PDF process-death recovery
#   tools/replica-stack.sh [replicas] --bench [s] [c] # load baseline, s seconds x c concurrency
#   ... --capacity                                  # k6 arrival-rate run (RATE, CAPACITY_SECONDS, ROUTES env)
#   ... --workers N                                 # hold workers constant while scaling APIs
#   ... --pgbouncer [transaction|session]             # api/worker connect through a PgBouncer container
#   ... --pg-cpus N                                   # the CPU quota Postgres gets (default 2)
# Postgres runs with pg_stat_statements loaded, so the bench reports the
# statements the database ran per request.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
replicas=${1:-2}; shift || true
case "$replicas" in ''|*[!0-9]*) echo "replicas must be a number"; exit 1;; esac
workers=$replicas
mode=suite; seconds=15; concurrency=32; pgbouncer=""; pg_cpus=${FLEET_PG_CPUS:-2}
while [ $# -gt 0 ]; do
  case "$1" in
    --reporting) mode=reporting; shift ;;
    --capacity) mode=capacity; shift ;;
    --workers) workers=$2; shift 2 ;;
    --bench) mode=bench; shift; [ $# -gt 0 ] && [[ "$1" != --* ]] && { seconds=$1; shift; }; [ $# -gt 0 ] && [[ "$1" != --* ]] && { concurrency=$1; shift; } ;;
    --pgbouncer) pgbouncer=transaction; shift; [ $# -gt 0 ] && [[ "$1" != --* ]] && { pgbouncer=$1; shift; } ;;
    --pg-cpus) pg_cpus=$2; shift 2 ;;
    *) echo "unknown argument: $1"; exit 1 ;;
  esac
done

test_args=()
if [ "$mode" = reporting ]; then
  export LOCINTEL_REPORTING_FLEET=1
  test_args=(--filter FullyQualifiedName~ReportingFleetTests)
fi

case "$workers" in ''|*[!0-9]*) echo "workers must be a non-negative integer"; exit 1;; esac
[ "$replicas" -ge 1 ] || { echo "replicas must be positive"; exit 1; }
if [ "$mode" = capacity ]; then command -v "${K6:-k6}" >/dev/null || { echo "k6 is required (set K6 to its executable)"; exit 1; }; fi
pg_port=${FLEET_PG_PORT:-55433}; bouncer_port=${FLEET_PGBOUNCER_PORT:-56432}; proxy_port=${FLEET_PROXY_PORT:-5300}
api_base=${FLEET_API_PORT_BASE:-5301}; worker_base=${FLEET_WORKER_PORT_BASE:-5401}
owner_cs="Host=localhost;Port=$pg_port;Database=locintel;Username=postgres;Password=owner"
log_dir=$(mktemp -d "${TMPDIR:-/tmp}/locintel-fleet.XXXXXX")
echo "Fleet logs: $log_dir"
pids=()
cleanup() {
  status=$?
  set +e
  for pid in "${pids[@]:-}"; do [ -n "$pid" ] && kill "$pid" 2>/dev/null; done
  rm -f "$log_dir/fixture.local.json"
  docker rm -f fleet-pg fleet-pgbouncer >/dev/null 2>&1 || true
  docker network rm fleet-net >/dev/null 2>&1 || true
  [ "$status" -ne 0 ] && echo "Failure diagnostics: $log_dir"
  return "$status"
}
trap cleanup EXIT

docker rm -f fleet-pg fleet-pgbouncer >/dev/null 2>&1 || true
docker network rm fleet-net >/dev/null 2>&1 || true
docker network create fleet-net >/dev/null
source "$root/tools/postgres-image.sh"
# connections are budgeted across the fleet (docs/production.md): Postgres
# sized for it, and each process given its share - four replicas of each role
# against the image's default hundred was the first thing the bench found.
# With PgBouncer in front, Postgres keeps the default hundred and every
# process gets a pool bigger than its share: the bouncer queues what the
# server cannot take, where a bare server refuses it (53300).
if [ -n "$pgbouncer" ]; then pg_max_connections=${FLEET_PG_MAX_CONNECTIONS:-100}; else pg_max_connections=${FLEET_PG_MAX_CONNECTIONS:-300}; fi
docker run -d --name fleet-pg --network fleet-net --cpus "$pg_cpus" -p "127.0.0.1:$pg_port:5432" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=locintel "$LOCINTEL_POSTGRES_IMAGE" \
  -c "max_connections=$pg_max_connections" -c shared_preload_libraries=pg_stat_statements -c pg_stat_statements.track=all >/dev/null
for _ in $(seq 1 60); do docker exec fleet-pg pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1 && break; sleep 1; done
docker exec fleet-pg psql -U postgres -d locintel -q -c "CREATE EXTENSION IF NOT EXISTS pg_stat_statements" >/dev/null

cd "$root"
dotnet build src/LocIntel.Api -c Release -nologo -v q
export ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__locintel="$owner_cs" Secrets__LocalMasterKey="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
ROLE=migrate ASPNETCORE_URLS="http://127.0.0.1:0" dotnet run --project src/LocIntel.Api -c Release --no-build --no-launch-profile > "$log_dir/migrate.log" 2>&1
if [ -n "$pgbouncer" ]; then
  # the bouncer owns the server budget; each process asks for more than its
  # share on purpose, which is exactly the situation a bare server refuses
  docker run -d --name fleet-pgbouncer --network fleet-net -p "127.0.0.1:$bouncer_port:5432" \
    -e DB_HOST=fleet-pg -e DB_PORT=5432 -e DB_USER=postgres -e DB_PASSWORD=owner -e DB_NAME=locintel \
    -e AUTH_TYPE=scram-sha-256 -e AUTH_USER=postgres -e "AUTH_QUERY=SELECT usename, passwd FROM pg_shadow WHERE usename=\$1" \
    -e POOL_MODE="$pgbouncer" -e MAX_CLIENT_CONN=2000 -e DEFAULT_POOL_SIZE=$((pg_max_connections - 20)) -e MAX_DB_CONNECTIONS=$((pg_max_connections - 20)) \
    -e IGNORE_STARTUP_PARAMETERS=extra_float_digits,search_path -e MAX_PREPARED_STATEMENTS=200 \
    "${FLEET_PGBOUNCER_IMAGE:-edoburu/pgbouncer:v1.24.1-p1}" > "$log_dir/pgbouncer.log" 2>&1
  for _ in $(seq 1 30); do (exec 3<>/dev/tcp/127.0.0.1/$bouncer_port) 2>/dev/null && break; sleep 1; done
  # session mode hands a server connection to a client for as long as the
  # client keeps it, so idle client connections must go back quickly or the
  # first processes to fill their pools starve the rest (the workers never
  # became ready with Npgsql's default five minutes)
  app_cs="Host=localhost;Port=$bouncer_port;Database=locintel;Username=postgres;Password=owner;Connection Idle Lifetime=5;Connection Pruning Interval=1"
  pool_size=100
  # the message store bypasses the bouncer (ADR 53): advisory locks and node
  # agents are session state. (A hyphen in the name: env, not export.)
  app_env=("ConnectionStrings__locintel-messaging=$owner_cs;Maximum Pool Size=10")
  echo "pgbouncer: $pgbouncer mode, server budget $((pg_max_connections - 20)), each process asks for $pool_size"
else
  app_env=("LOCINTEL_FLEET=direct") # a non-empty array: bash 3 treats an empty one as unbound under set -u
  app_cs="$owner_cs"
  pool_size=$(( (pg_max_connections - 20) / (replicas + workers) ))
  [ "$pool_size" -gt 40 ] && pool_size=40
fi
export ConnectionStrings__locintel="$app_cs;Maximum Pool Size=$pool_size;Max Auto Prepare=${FLEET_AUTO_PREPARE:-0}"
export Auth__Provider=local Database__AppUser=app_user Database__AppPassword=app_user
export Storage__LocalRoot="${TMPDIR:-/tmp}/locintel-fleet-store" Logging__LogLevel__Default=Warning
# the proxy is the request host the api sees, so CSRF's origin check and every URL it builds agree
export Proxy__TrustForwardedHeaders=true Proxy__KnownProxies__0=127.0.0.1
export AllowedHosts="localhost;127.0.0.1;*.localhost;*.locintel.test"

wait_ready() { # name port
  for _ in $(seq 1 120); do curl -fsS "http://127.0.0.1:$2/healthz" 2>/dev/null | grep -q '"status":"ok"' && return 0; sleep 1; done
  echo "$1 never became ready"; tail -40 "$log_dir/$1.log"; exit 1
}
app_env+=("Logging__LogLevel__LocIntel.Modules.Reporting.ReportRunner=Information")
upstreams=()
api_pids=()
for i in $(seq 1 "$replicas"); do
  port=$((api_base + i - 1))
  # --no-launch-profile: launchSettings.json would pin every replica to the same port
  env "${app_env[@]}" ROLE=api ASPNETCORE_URLS="http://127.0.0.1:$port" dotnet "$root/src/LocIntel.Api/bin/Release/net10.0/LocIntel.Api.dll" > "$log_dir/api-$i.log" 2>&1 &
  pids+=($!); api_pids+=($!)
  # the first replica seeds the dev data before the others boot (the seed is idempotent, not concurrent)
  wait_ready "api-$i" "$port"
  upstreams+=("http://127.0.0.1:$port")
done
worker_pids=()
for i in $(seq 1 "$workers"); do
  port=$((worker_base + i - 1))
  env "${app_env[@]}" ROLE=worker ASPNETCORE_URLS="http://127.0.0.1:$port" dotnet "$root/src/LocIntel.Api/bin/Release/net10.0/LocIntel.Api.dll" > "$log_dir/worker-$i.log" 2>&1 &
  pids+=($!); worker_pids+=($!)
done
for i in $(seq 1 "$workers"); do wait_ready "worker-$i" $((worker_base + i - 1)); done
node "$root/tools/replica-proxy.mjs" "$proxy_port" "${upstreams[@]}" > "$log_dir/proxy.log" 2>&1 &
pids+=($!)
for _ in $(seq 1 30); do curl -fsS "http://127.0.0.1:$proxy_port/healthz" >/dev/null 2>&1 && break; sleep 1; done
echo "fleet: $replicas api + $workers worker behind http://127.0.0.1:$proxy_port"

if [ "$mode" = capacity ]; then
  FLEET_BENCH_FIXTURE="$log_dir/fixture.local.json" node "$root/tools/fleet-bench.mjs" "http://127.0.0.1:$proxy_port" 15 32 "$replicas"
  export FIXTURE="$log_dir/fixture.local.json" FLEET_LOG_DIR="$log_dir"
  export FLEET_PROCESS_IDS="$(IFS=,; echo "${pids[*]}")"
  export FLEET_API_IDS="$(IFS=,; echo "${api_pids[*]}")"
  export FLEET_REPLICAS="$replicas" FLEET_WORKERS="$workers" FLEET_PG_CPUS="$pg_cpus" FLEET_POOL_SIZE="$pool_size" FLEET_POOLER="$pgbouncer"
  "$root/tools/capacity-local.sh"
elif [ "$mode" = bench ]; then
  LOCINTEL_PG_STATS_CMD="docker exec fleet-pg psql -U postgres -d locintel -tA -c" \
  node "$root/tools/fleet-bench.mjs" "http://127.0.0.1:$proxy_port" "$seconds" "$concurrency" "$replicas${pgbouncer:+ (pgbouncer $pgbouncer)}"
else
  LOCINTEL_FLEET_URL="http://127.0.0.1:$proxy_port" \
  LOCINTEL_FLEET_PG="$owner_cs" \
  LOCINTEL_FLEET_REPLICAS="$replicas" \
  LOCINTEL_FLEET_REPORT_LOG_DIR="$log_dir" \
  LOCINTEL_FLEET_WORKER_PIDS="$(IFS=,; echo "${worker_pids[*]}")" \
  LOCINTEL_FLEET_WORKER_PORTS="$(seq -s, "$worker_base" $((worker_base + workers - 1)))" \
  dotnet test tests/LocIntel.FleetTests -c Release --logger "console;verbosity=normal" ${test_args[@]+"${test_args[@]}"}
fi
