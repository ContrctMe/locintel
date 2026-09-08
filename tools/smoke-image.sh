#!/usr/bin/env bash
# Boot the three roles from ONE image in Production mode against a real
# PostgreSQL, the way an orchestrator would: migrate runs to completion with
# owner credentials, then api and worker start as app_user and must answer
# both probes. The worker must complete a durable cleanup before the API starts.
# Every adapter seam is on a production provider (registration
# is lazy, nothing is contacted), so this is also the boot guards' negative
# control. Usage: tools/smoke-image.sh <image>
set -euo pipefail
image="${1:?image}"
net="locintel-smoke-$$"
for name in smoke-pg smoke-api smoke-worker; do
  if docker container inspect "$name" >/dev/null 2>&1; then
    echo "$name already exists; refusing to replace or remove it" >&2
    exit 1
  fi
done
secrets_dir=$(mktemp -d "${TMPDIR:-/tmp}/locintel-smoke-keys.XXXXXX")
keys_volume="$net-keys"
cleanup() {
  docker rm -f smoke-pg smoke-api smoke-worker >/dev/null 2>&1 || true
  docker network rm "$net" >/dev/null 2>&1 || true
  docker volume rm "$keys_volume" >/dev/null 2>&1 || true
  rm -rf "$secrets_dir"
}
trap cleanup EXIT
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN=locintel-image-smoke \
  -keyout "$secrets_dir/key.pem" -out "$secrets_dir/cert.pem" >/dev/null 2>&1
openssl pkcs12 -export -inkey "$secrets_dir/key.pem" -in "$secrets_dir/cert.pem" \
  -out "$secrets_dir/keyring.pfx" -passout pass: >/dev/null 2>&1
# The containing host directory remains 0700; mount only this ephemeral PFX file.
chmod 444 "$secrets_dir/keyring.pfx"
docker volume create "$keys_volume" >/dev/null
docker run --rm --network none --user 0 --entrypoint chown \
  -v "$keys_volume:/keys" "$image" 1654:1654 /keys
docker network create "$net" >/dev/null
source "$(cd "$(dirname "$0")" && pwd)/postgres-image.sh"
docker run -d --name smoke-pg --network "$net" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=locintel "$LOCINTEL_POSTGRES_IMAGE" >/dev/null
# Over TCP, not the socket: the image's first-time init runs a temporary
# server on the socket alone (PostGIS extension scripts take seconds), so a
# socket check says "ready" before the real server is up (ADR 50).
for _ in $(seq 1 60); do docker exec smoke-pg pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1 && break; sleep 1; done

common=(
  --network "$net"
  --read-only --cap-drop ALL --security-opt no-new-privileges
  --tmpfs /tmp:rw,nosuid,size=268435456
  -v "$keys_volume:/keys"
  -v "$secrets_dir/keyring.pfx:/certificate/keyring.pfx:ro"
  -e ASPNETCORE_ENVIRONMENT=Production
  -e "ConnectionStrings__locintel=Host=smoke-pg;Database=locintel;Username=app_user;Password=app_user"
  -e Database__AppUser=app_user -e Database__AppPassword=app_user
  -e DataProtection__KeyPath=/keys
  -e DataProtection__CertificatePath=/certificate/keyring.pfx
  -e Auth__Provider=workos -e Auth__WorkOS__ApiKey=sk_unused -e Auth__WorkOS__ClientId=client_unused
  -e Auth__WorkOS__WebhookSecret=whsec_unused
  -e "AllowedHosts=localhost;127.0.0.1;*.example.test"
  -e Proxy__KnownProxies__0=127.0.0.1
  -e Storage__Provider=s3 -e Storage__S3__BucketName=unused
  -e Storage__S3__ServiceUrl=http://127.0.0.1:1 -e Storage__S3__AccessKey=unused -e Storage__S3__SecretKey=unused
  -e Scanner__Provider=clamav -e Scanner__ClamAv__Host=unused
  -e Secrets__Provider=kms -e Secrets__Kms__KeyId=unused
  -e Secrets__Kms__ServiceUrl=http://127.0.0.1:1 -e Secrets__Kms__AccessKey=unused -e Secrets__Kms__SecretKey=unused
  -e Billing__Provider=stripe -e Billing__Stripe__ApiKey=sk_unused -e Billing__Stripe__WebhookSecret=whsec_unused
  -e Billing__Stripe__PriceIds__growth=price_unused -e Billing__Stripe__PriceIds__scale=price_unused
  -e Notifications__Transport=smtp -e Notifications__Smtp__Host=unused -e Notifications__Smtp__FromAddress=noreply@example.test
  -e Public__HostTemplate=https://{slug}.example.test
  -e Build__Version=smoke
)

echo "== migrate"
docker run --rm "${common[@]}" -e ROLE=migrate \
  -e "ConnectionStrings__locintel=Host=smoke-pg;Database=locintel;Username=postgres;Password=owner" "$image"

probe() { # name port
  local name=$1 port=$2
  for _ in $(seq 1 90); do
    assert_running "$name"
    curl -fsS "http://localhost:$port/livez" >/dev/null 2>&1 && break
    sleep 1
  done
  curl -fsS "http://localhost:$port/livez" | grep -q '"status":"alive"' || { echo "$name: no liveness"; docker logs "$name"; exit 1; }
  for _ in $(seq 1 60); do
    assert_running "$name"
    curl -fsS "http://localhost:$port/healthz" 2>/dev/null | grep -q '"status":"ok"' && break
    sleep 1
  done
  curl -fsS "http://localhost:$port/healthz" | grep -q "\"role\":\"${name#smoke-}\"" || { echo "$name: not ready"; docker logs "$name"; exit 1; }
  echo "$name: alive and ready"
}
assert_running() {
  [ "$(docker inspect -f '{{.State.Running}}' "$1")" = true ] || {
    echo "$1 exited unexpectedly"; docker logs "$1"; exit 1;
  }
}
assert_nonroot() {
  local uid
  uid=$(docker exec "$1" id -u)
  [[ "$uid" =~ ^[0-9]+$ && "$uid" != 0 ]] || { echo "$1 is not running as a non-root user"; exit 1; }
  docker exec "$1" sh -c 'grep -Eq "^Seccomp:[[:space:]]+2$" /proc/self/status' || {
    echo "$1 has no active seccomp filter"; exit 1;
  }
  docker exec "$1" sh -c 'test ! -w /app && test -w /tmp && test -w /keys' || {
    echo "$1 filesystem permissions do not match the hardened deployment"; exit 1;
  }
  docker exec "$1" dotnet --list-runtimes
}
sql() { docker exec smoke-pg psql -X -v ON_ERROR_STOP=1 -U postgres -d locintel -Atc "$1"; }

# Only the worker is running when this row disappears. CleanupIdempotency is
# published through UseDurableLocalQueues and handled through the generated
# pipeline; the fresh row proves that successful processing respects its TTL.
sql "INSERT INTO platform.idempotency_keys (org_id, key, endpoint, request_hash, created_at)
     VALUES ('00000000-0000-0000-0000-000000000001', 'smoke-expired', 'smoke', 'smoke', now() - interval '25 hours'),
            ('00000000-0000-0000-0000-000000000001', 'smoke-fresh', 'smoke', 'smoke', now())" >/dev/null
echo "== worker"
docker run -d --name smoke-worker "${common[@]}" -p 127.0.0.1:18081:8080 -e ROLE=worker "$image" >/dev/null
assert_nonroot smoke-worker
probe smoke-worker 18081
for _ in $(seq 1 60); do
  assert_running smoke-worker
  if [ "$(sql "SELECT count(*) FROM platform.idempotency_keys WHERE key = 'smoke-expired'")" = 0 ] &&
     [ "$(sql "SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE message_type = 'LocIntel.Api.CleanupIdempotency' AND status = 'Handled'")" -ge 1 ]; then
    break
  fi
  sleep 1
done
[ "$(sql "SELECT count(*) FROM platform.idempotency_keys WHERE key = 'smoke-expired'")" = 0 ] || {
  echo "worker did not complete durable cleanup"; docker logs smoke-worker; exit 1;
}
[ "$(sql "SELECT count(*) FROM platform.idempotency_keys WHERE key = 'smoke-fresh'")" = 1 ] || {
  echo "worker removed an unexpired record"; exit 1;
}
[ "$(sql "SELECT count(*) FROM wolverine.wolverine_incoming_envelopes WHERE message_type = 'LocIntel.Api.CleanupIdempotency' AND status = 'Handled'")" -ge 1 ] || {
  echo "worker did not acknowledge the durable cleanup envelope"; exit 1;
}
[ "$(sql 'SELECT count(*) FROM wolverine.wolverine_dead_letters')" = 0 ] || {
  echo "worker produced dead letters"; docker logs smoke-worker; exit 1;
}
echo "worker completed durable cleanup and retained the unexpired record"

echo "== api"
docker run -d --name smoke-api "${common[@]}" -p 127.0.0.1:18080:8080 -e ROLE=api "$image" >/dev/null
assert_nonroot smoke-api
probe smoke-api 18080
for name in smoke-api smoke-worker; do
  assert_running "$name"
  if docker logs "$name" 2>&1 | grep -Ei 'exception|^fail:|^crit:'; then
    echo "$name logged a failure"; docker logs "$name"; exit 1
  fi
done
echo "all three roles verified from $image"
