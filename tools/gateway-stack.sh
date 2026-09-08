#!/usr/bin/env bash
# Portable local reference: up [api replicas] [gateway replicas], down, or compose ...
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
runtime="${GATEWAY_RUNTIME:-$root/coverage/gateway/runtime}"
source "$root/tools/postgres-image.sh"
export LOCINTEL_POSTGRES_IMAGE
action=${1:-up}; shift || true
if [ "$action" = up ]; then
  api_count=${1:-2}; gateway_count=${2:-2}
  for count in "$api_count" "$gateway_count"; do
    case "$count" in ''|*[!0-9]*) echo "replica counts must be positive integers" >&2; exit 1;; esac
    [ "$count" -ge 1 ] && [ "$count" -le 10 ] || { echo "local reference supports 1–10 replicas" >&2; exit 1; }
  done
  mkdir -p "$runtime/state/keys" "$runtime/state/storage" "$runtime/ratelimit/config"
  chmod 700 "$runtime"
  if [ ! -f "$runtime/.env" ]; then
    python3 - "$runtime" <<'PY'
import os, pathlib, secrets, sys
root = pathlib.Path(sys.argv[1])
env = f'LOCAL_UID={os.getuid()}\nGATEWAY_RUNTIME={root}\nGATEWAY_IDENTITY_KEY={secrets.token_hex(32)}\n'
(root / '.env').write_text(env)
(root / '.env').chmod(0o600)
PY
  fi
  python3 - "$root" "$runtime" <<'PY'
import pathlib, sys
root, runtime = map(pathlib.Path, sys.argv[1:])
settings = dict(line.split('=', 1) for line in (runtime / '.env').read_text().splitlines())
template = (root / 'deploy/gateway/envoy.yaml').read_text()
(runtime / 'envoy.yaml').write_text(template.replace('__GATEWAY_IDENTITY_KEY__', settings['GATEWAY_IDENTITY_KEY']))
policy = runtime / 'ratelimit/config/policy.yaml'
if not policy.exists():
    policy.write_text((root / 'deploy/gateway/policy.yaml').read_text())
PY
  architecture=$(docker info --format '{{.Architecture}}')
  case "$architecture" in aarch64|arm64) architecture=arm64;; x86_64|amd64) architecture=x64;; *) echo "unsupported Docker architecture: $architecture"; exit 1;; esac
  dotnet publish "$root/src/LocIntel.Api" -c Release --os linux --arch "$architecture" -p:PublishProfile=DefaultContainer -p:ContainerImageTag=gateway-dev -v q
fi
compose=(docker compose --env-file "$runtime/.env" -f "$root/deploy/gateway/compose.yaml")
wait_apis() {
  local containers container ip ready
  containers=$("${compose[@]}" ps -a -q api)
  [ -n "$containers" ] || { echo "no API replicas" >&2; return 1; }
  for container in $containers; do
    ip=$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$container")
    ready=false
    for _ in $(seq 1 120); do
      if "${compose[@]}" exec -T redis wget -q -T 2 --header="Host: localhost" -O /dev/null "http://$ip:8080/healthz" 2>/dev/null; then ready=true; break; fi
      sleep 1
    done
    [ "$ready" = true ] || { echo "API $container did not become ready" >&2; exit 1; }
  done
}
case "$action" in
  up)
    # This local runner uses a drained upgrade: the retirement migration must
    # not race an older API/worker binary still using the removed counter table.
    "${compose[@]}" stop api worker gateway
    # First boot seeds dev data once; scale only after the first API is ready.
    "${compose[@]}" up -d --scale api=1 --scale gateway=1
    # Bootstrap configuration is read at process start, not watched in-place.
    "${compose[@]}" restart gateway
    address=$("${compose[@]}" port --index 1 gateway 8080)
    ready=false
    for _ in $(seq 1 120); do
      if curl -fsS "http://$address/healthz" >/dev/null 2>&1; then ready=true; break; fi
      sleep 1
    done
    [ "$ready" = true ] || { echo "gateway did not become ready; inspect tools/gateway-stack.sh compose logs" >&2; exit 1; }
    "${compose[@]}" up -d --scale api="$api_count" --scale gateway="$gateway_count"
    wait_apis
    for index in $(seq 1 "$gateway_count"); do printf 'Gateway %s: http://%s\n' "$index" "$("${compose[@]}" port --index "$index" gateway 8080)"; done
    ;;
  wait-api) wait_apis ;;
  down) "${compose[@]}" down ;;
  compose) "${compose[@]}" "$@" ;;
  *) echo "usage: $0 up [api replicas] [gateway replicas] | wait-api | down | compose ..." >&2; exit 1;;
esac
