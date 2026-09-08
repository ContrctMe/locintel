#!/usr/bin/env bash
# Boot the stack a browser run needs, run the Playwright suite, tear down.
# Postgres in Docker; the migrate role, then the api role in Development with
# the LOCAL auth provider (sign-in by hint, no credentials typed - the same
# password-less boot LOCINTEL_AUTH=local gives the AppHost); the built console
# and public SSR artifacts. Usage: tools/e2e-stack.sh [playwright args]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
# Each engine needs its own seeded accounts and rate-limit window. Reusing one
# stack makes later projects inherit earlier projects' traffic and mutations.
selected_project=false
for arg in "$@"; do
  case "$arg" in --project|--project=*) selected_project=true ;; esac
done
if [ "$selected_project" = false ]; then
  for engine in chromium firefox webkit; do
    "$root/tools/e2e-stack.sh" "$@" "--project=$engine"
  done
  exit 0
fi
pg_port=${E2E_PG_PORT:-55432}; api_port=${E2E_API_PORT:-5293}
worker_port=${E2E_WORKER_PORT:-5294}
web_port=${E2E_WEB_PORT:-5173}; public_port=${E2E_PUBLIC_PORT:-5174}
cs="Host=localhost;Port=$pg_port;Database=locintel;Username=postgres;Password=owner"
log_dir=$(mktemp -d "${TMPDIR:-/tmp}/locintel-e2e.XXXXXX")
echo "Stack logs: $log_dir"
cleanup() {
  status=$?
  set +e
  rm -f "$log_dir/public.key" "$log_dir/public.crt"
  if [ "$status" -ne 0 ]; then
    # Capture before teardown so connection-reset noise cannot obscure the
    # actual failure. Playwright clears test-results at startup: copy only now.
    docker logs e2e-pg > "$log_dir/postgres.log" 2>&1
    diagnostics="$root/web/tools/e2e/test-results/stack-logs/$(basename "$log_dir")"
    mkdir -p "$diagnostics"
    cp -R "$log_dir/." "$diagnostics/"
    echo "Failure diagnostics: $diagnostics"
  fi
  [ -n "${api_pid:-}" ] && kill "$api_pid" 2>/dev/null || true
  [ -n "${worker_pid:-}" ] && kill "$worker_pid" 2>/dev/null || true
  [ -n "${web_pid:-}" ] && kill "$web_pid" 2>/dev/null || true
  [ -n "${public_pid:-}" ] && kill "$public_pid" 2>/dev/null || true
  docker rm -f e2e-pg >/dev/null 2>&1 || true
  return "$status"
}
trap cleanup EXIT
docker rm -f e2e-pg >/dev/null 2>&1 || true
source "$root/tools/postgres-image.sh"
docker run -d --name e2e-pg -p "127.0.0.1:$pg_port:5432" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=locintel "$LOCINTEL_POSTGRES_IMAGE" >/dev/null
# Over TCP, not the socket: the image's first-time init runs a temporary
# server on the socket alone (PostGIS extension scripts take seconds), so a
# socket check says "ready" before the real server is up (ADR 50).
for _ in $(seq 1 60); do docker exec e2e-pg pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1 && break; sleep 1; done

cd "$root"
dotnet build src/LocIntel.Api -c Release -nologo -v q
export ASPNETCORE_ENVIRONMENT=Development ConnectionStrings__locintel="$cs" Secrets__LocalMasterKey="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
ROLE=migrate ASPNETCORE_URLS="http://127.0.0.1:0" dotnet run --project src/LocIntel.Api -c Release --no-build --no-launch-profile
export ROLE=api Auth__Provider=local Database__AppUser=app_user Database__AppPassword=app_user
export Storage__LocalRoot="${TMPDIR:-/tmp}/locintel-e2e-store"
export ASPNETCORE_URLS="http://localhost:$api_port"
dotnet run --project src/LocIntel.Api -c Release --no-build --no-launch-profile > "$log_dir/api.log" 2>&1 &
api_pid=$!
for _ in $(seq 1 120); do curl -fsS "http://localhost:$api_port/healthz" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://localhost:$api_port/healthz" | grep -q '"status":"ok"' || { echo "api never became ready"; tail -50 "$log_dir/api.log"; exit 1; }

ROLE=worker ASPNETCORE_URLS="http://127.0.0.1:$worker_port" dotnet run --project src/LocIntel.Api -c Release --no-build --no-launch-profile > "$log_dir/worker.log" 2>&1 &
worker_pid=$!
for _ in $(seq 1 120); do curl -fsS "http://127.0.0.1:$worker_port/healthz" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$worker_port/healthz" | grep -q '"role":"worker"' || { echo "worker never became ready"; tail -50 "$log_dir/worker.log"; exit 1; }

cd "$root/web"
pnpm --filter @locintel/console build
LOCINTEL_API="http://localhost:$api_port" pnpm --filter @locintel/public build
LOCINTEL_API="http://localhost:$api_port" pnpm --filter @locintel/console exec vite preview --host 127.0.0.1 --port "$web_port" > "$log_dir/console.log" 2>&1 &
web_pid=$!
# Exercise production Secure cookies over TLS: WebKit correctly refuses them over HTTP localhost.
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj '/CN=acme-dev.localhost' \
  -keyout "$log_dir/public.key" -out "$log_dir/public.crt" > /dev/null 2>&1
LOCINTEL_API="http://localhost:$api_port" pnpm --filter @locintel/public start --host 127.0.0.1 --port "$public_port" \
  --tls --cert "$log_dir/public.crt" --key "$log_dir/public.key" > "$log_dir/public.log" 2>&1 &
public_pid=$!
for _ in $(seq 1 60); do curl -fsS "http://localhost:$web_port/" >/dev/null 2>&1 && break; sleep 1; done
for _ in $(seq 1 60); do curl -kfsS "https://acme-dev.localhost:$public_port/" >/dev/null 2>&1 && break; sleep 1; done
curl -kfsS "https://acme-dev.localhost:$public_port/" >/dev/null || { echo "public app never became ready"; tail -50 "$log_dir/public.log"; exit 1; }
if [ "${E2E_LOADING_BASELINE:-0}" = 1 ]; then
  E2E_CONSOLE="http://localhost:$web_port" E2E_PUBLIC="https://acme-dev.localhost:$public_port" pnpm --filter @locintel/e2e exec node loading-baseline.mjs
else
  E2E_CONSOLE="http://localhost:$web_port" E2E_PUBLIC="https://acme-dev.localhost:$public_port" pnpm test:e2e "$@"
fi
