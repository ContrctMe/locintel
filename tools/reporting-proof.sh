#!/usr/bin/env bash
# Run the PDF library in the production runtime, without network or host fonts.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
image="${1:-locintel:compat-dev}"
output="${2:-$(mktemp -d "${TMPDIR:-/tmp}/locintel-reporting.XXXXXX")}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
publish="$(mktemp -d "${TMPDIR:-/tmp}/locintel-reporting-publish.XXXXXX")"
container="locintel-reporting-proof-$$"
cleanup() {
  docker rm -f "$container" >/dev/null 2>&1 || true
  rm -rf "$publish"
}
trap cleanup EXIT
dotnet publish "$root/tools/LocIntel.Tools.ReportingProof" -c Release \
  -r linux-x64 --self-contained false -o "$publish" -v quiet
docker run --rm --name "$container" --platform linux/amd64 \
  --network none --read-only --memory 256m --cpus 1 --pids-limit 128 --ulimit core=0 \
  --user "$(id -u):$(id -g)" --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  -v "$publish:/proof:ro" -v "$output:/output" \
  --entrypoint dotnet "$image" /proof/LocIntel.Tools.ReportingProof.dll /output \
  | tee "$output/metrics.json"
printf 'Reporting proof artifacts: %s\n' "$output"
