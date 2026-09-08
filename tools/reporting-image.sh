#!/usr/bin/env bash
# Qualify actual reference jobs from the OCI image in Production. Local-only
# fixtures bootstrap a session; no external auth, basemap or paid service is used.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
image="${1:?usage: bash tools/reporting-image.sh IMAGE [OUTPUT]}"
platform="$(docker image inspect "$image" --format '{{.Os}}/{{.Architecture}}')"
output="${2:-$(mktemp -d "${TMPDIR:-/tmp}/locintel-reporting-image.XXXXXX")}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
chmod 700 "$output"
prefix="locintel-report-image-$$"
net="$prefix-net"; keys="$prefix-keys"
secrets_dir=$(mktemp -d "${TMPDIR:-/tmp}/locintel-reporting-image-keys.XXXXXX")
containers=()
cleanup() {
  local status=$?
  set +e
  for ((i=${#containers[@]}-1; i>=0; i--)); do
    name="${containers[i]}"
    docker logs "$name" > "$output/$name.log" 2>&1
    docker rm -f "$name" >/dev/null
  done
  docker volume rm "$keys" >/dev/null 2>&1
  docker network rm "$net" >/dev/null 2>&1
  rm -f "$output/session.cookies"
  rm -rf "$secrets_dir"
  echo "Reporting image evidence: $output"
  return "$status"
}
trap cleanup EXIT
# Trust this run's local S3 server explicitly; never disable TLS verification.
mkdir "$secrets_dir/minio"
cat > "$secrets_dir/ca.cnf" <<'EOF'
[req]
distinguished_name=dn
x509_extensions=ca
[dn]
[ca]
basicConstraints=critical,CA:TRUE
keyUsage=critical,keyCertSign,cRLSign
subjectKeyIdentifier=hash
EOF
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -config "$secrets_dir/ca.cnf" -subj /CN=reporting-fixture-ca \
  -keyout "$secrets_dir/ca.key" -out "$secrets_dir/ca.pem" >/dev/null 2>&1
openssl req -newkey rsa:2048 -nodes -subj /CN=report-s3 \
  -keyout "$secrets_dir/minio/private.key" -out "$secrets_dir/s3.csr" >/dev/null 2>&1
cat > "$secrets_dir/s3.ext" <<'EOF'
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
subjectAltName=DNS:report-s3,IP:127.0.0.1
EOF
openssl x509 -req -in "$secrets_dir/s3.csr" -CA "$secrets_dir/ca.pem" \
  -CAkey "$secrets_dir/ca.key" -CAcreateserial -days 1 -extfile "$secrets_dir/s3.ext" \
  -out "$secrets_dir/minio/public.crt" >/dev/null 2>&1
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN=reporting-keyring \
  -keyout "$secrets_dir/keyring.key" -out "$secrets_dir/keyring.pem" >/dev/null 2>&1
openssl pkcs12 -export -inkey "$secrets_dir/keyring.key" -in "$secrets_dir/keyring.pem" \
  -out "$secrets_dir/keyring.pfx" -passout pass: >/dev/null 2>&1
# Preserve the image's normal trust roots and add only the local fixture CA.
docker run --rm --platform "$platform" --network none --read-only --cap-drop ALL \
  --security-opt no-new-privileges --entrypoint cat "$image" /etc/ssl/certs/ca-certificates.crt \
  > "$secrets_dir/trust.pem"
cat "$secrets_dir/ca.pem" >> "$secrets_dir/trust.pem"
chmod 755 "$secrets_dir/minio"
chmod 444 "$secrets_dir/minio/private.key" "$secrets_dir/minio/public.crt" \
  "$secrets_dir/keyring.pfx" "$secrets_dir/trust.pem"
docker network create "$net" >/dev/null
docker volume create "$keys" >/dev/null
docker run --rm --platform "$platform" --network none --user root \
  -v "$keys:/keys" --entrypoint chown "$image" 1654:1654 /keys
docker image inspect "$image" --format '{{json .}}' > "$output/image.json"
source "$root/tools/postgres-image.sh"
containers+=("$prefix-pg")
docker run -d --name "$prefix-pg" --network "$net" -e POSTGRES_PASSWORD=owner -e POSTGRES_DB=locintel "$LOCINTEL_POSTGRES_IMAGE" >/dev/null
for _ in $(seq 1 60); do docker exec "$prefix-pg" pg_isready -h 127.0.0.1 -U postgres >/dev/null 2>&1 && break; sleep 1; done
containers+=("$prefix-s3")
docker run -d --name "$prefix-s3" --network "$net" --network-alias report-s3 -p 127.0.0.1::9000 \
  -v "$secrets_dir/minio:/certs:ro" -e MINIO_ROOT_USER=fixture-access -e MINIO_ROOT_PASSWORD=fixture-secret \
  minio/minio:latest server --certs-dir /certs /data >/dev/null
s3_port="$(docker port "$prefix-s3" 9000/tcp | cut -d: -f2)"
for _ in $(seq 1 60); do curl -fsS --cacert "$secrets_dir/ca.pem" "https://127.0.0.1:$s3_port/minio/health/ready" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS --cacert "$secrets_dir/ca.pem" --aws-sigv4 'aws:amz:us-east-1:s3' \
  --user fixture-access:fixture-secret -X PUT "https://127.0.0.1:$s3_port/reports"
common=(--platform "$platform" --network "$net" --read-only --memory 768m --cpus 1 --pids-limit 256 --ulimit core=0
  --cap-drop ALL --security-opt no-new-privileges
  --tmpfs /tmp:rw,nosuid,size=256m -v "$keys:/keys"
  -v "$secrets_dir/keyring.pfx:/certificate/keyring.pfx:ro"
  -v "$secrets_dir/trust.pem:/certificate/trust.pem:ro" -e SSL_CERT_FILE=/certificate/trust.pem
  -e "ConnectionStrings__locintel=Host=$prefix-pg;Database=locintel;Username=app_user;Password=app_user;Maximum Pool Size=20"
  -e Database__AppUser=app_user -e Database__AppPassword=app_user -e DataProtection__KeyPath=/keys
  -e DataProtection__CertificatePath=/certificate/keyring.pfx
  -e "AllowedHosts=localhost;127.0.0.1;*.example.test"
  -e Storage__Provider=s3 -e Storage__S3__BucketName=reports -e Storage__S3__ServiceUrl=https://report-s3:9000
  -e Storage__S3__AccessKey=fixture-access -e Storage__S3__SecretKey=fixture-secret -e Storage__S3__ForcePathStyle=true)
production=(-e ASPNETCORE_ENVIRONMENT=Production
  -e Auth__Provider=workos -e Auth__WorkOS__ApiKey=sk_unused -e Auth__WorkOS__ClientId=client_unused
  -e Auth__WorkOS__WebhookSecret=whsec_unused
  -e Scanner__Provider=clamav -e Scanner__ClamAv__Host=unused
  -e Secrets__Provider=kms -e Secrets__Kms__KeyId=unused
  -e Billing__Provider=stripe -e Billing__Stripe__ApiKey=sk_unused -e Billing__Stripe__WebhookSecret=whsec_unused
  -e Billing__Stripe__PriceIds__growth=price_unused -e Billing__Stripe__PriceIds__scale=price_unused
  -e Notifications__Transport=smtp -e Notifications__Smtp__Host=unused -e Notifications__Smtp__FromAddress=noreply@example.test
  -e Public__HostTemplate=https://{slug}.example.test)
docker run --rm "${common[@]}" "${production[@]}" -e ROLE=migrate \
  -e "ConnectionStrings__locintel=Host=$prefix-pg;Database=locintel;Username=postgres;Password=owner;Maximum Pool Size=20" \
  "$image" > "$output/migrate.log" 2>&1
wait_ready() {
  local name=$1 port=$2
  for _ in $(seq 1 120); do
    [ "$(docker inspect -f '{{.State.Running}}' "$name")" = true ] || { docker logs "$name"; return 1; }
    if curl -fsS "http://127.0.0.1:$port/healthz" > "$output/$name-health.json" 2>/dev/null; then return; fi
    sleep 1
  done
  docker logs "$name"; return 1
}
# Only fixture setup uses Development. The same image then restarts as Production.
containers+=("$prefix-api")
docker run -d --name "$prefix-api" "${common[@]}" -p 127.0.0.1::8080 -e ROLE=api \
  -e ASPNETCORE_ENVIRONMENT=Development -e Auth__Provider=local \
  -e Secrets__LocalMasterKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA= "$image" >/dev/null
api_port="$(docker port "$prefix-api" 8080/tcp | cut -d: -f2)"
wait_ready "$prefix-api" "$api_port"
python3 "$root/tools/reporting-image.py" prepare "http://127.0.0.1:$api_port" "$s3_port" "$output" "$secrets_dir/ca.pem"
docker logs "$prefix-api" > "$output/bootstrap.log" 2>&1
docker rm -f "$prefix-api" >/dev/null
docker run -d --name "$prefix-api" "${common[@]}" "${production[@]}" -p "127.0.0.1:$api_port:8080" -e ROLE=api "$image" >/dev/null
wait_ready "$prefix-api" "$api_port"
containers+=("$prefix-worker")
docker run -d --name "$prefix-worker" "${common[@]}" "${production[@]}" -p 127.0.0.1::8080 -e ROLE=worker "$image" >/dev/null
worker_port="$(docker port "$prefix-worker" 8080/tcp | cut -d: -f2)"
wait_ready "$prefix-worker" "$worker_port"
for name in "$prefix-api" "$prefix-worker"; do
  [ "$(docker exec "$name" id -u)" != 0 ] || { echo 'root process refused'; exit 1; }
  docker exec "$name" sh -c 'grep -Eq "^Seccomp:[[:space:]]+2$" /proc/self/status &&
    grep -Eq "^NoNewPrivs:[[:space:]]+1$" /proc/self/status &&
    grep -Eq "^CapEff:[[:space:]]+0+$" /proc/self/status &&
    test ! -w /app && test -w /tmp && test -w /keys' || {
    echo "$name failed the runtime security checks"; exit 1;
  }
done
docker exec "$prefix-pg" psql -X -v ON_ERROR_STOP=1 -U postgres -d locintel -Atc \
  "SELECT EXISTS (SELECT FROM pg_stat_activity WHERE usename = 'app_user')
     AND NOT EXISTS (SELECT FROM pg_stat_activity WHERE usename = 'postgres' AND pid <> pg_backend_pid() AND backend_type = 'client backend')" \
  | tee "$output/application-role-check.txt" | grep -qx t
python3 "$root/tools/reporting-image.py" verify "http://127.0.0.1:$api_port" "$s3_port" "$output" "$secrets_dir/ca.pem"
docker exec "$prefix-pg" psql -X -v ON_ERROR_STOP=1 -U postgres -d locintel -Atc \
  "SELECT count(*) FROM wolverine.wolverine_dead_letters" | tee "$output/dead-letter-count.txt" | grep -qx 0
echo 'Production image: single, aggregate, 100-site ZIP, site-file publication and quota settlement passed.'
