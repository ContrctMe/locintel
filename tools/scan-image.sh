#!/usr/bin/env bash
# Trivy inspects the local artifact; only vulnerability databases are downloaded.
# No dependency snapshot or image is submitted to an external scanning service.
set -euo pipefail
image=${1:?usage: scan-image.sh IMAGE OUTPUT_DIRECTORY}
output=${2:?output directory}
mkdir -p "$output"
trivy --version > "$output/scanner-version.txt"
image=$(docker image inspect "$image" --format '{{.Id}}')
printf '%s\n' "$image" > "$output/image-id.txt"
docker image inspect "$image" --format '{{json .RepoDigests}}' > "$output/registry-digests.json"
docker image inspect "$image" --format '{{range .Config.Env}}{{println .}}{{end}}' \
  | grep -E '^(DOTNET_VERSION|ASPNET_VERSION)=' > "$output/runtime-versions.txt"
trivy image --image-src docker --scanners vuln --format cyclonedx \
  --output "$output/sbom.cdx.json" "$image"
trivy image --image-src docker --scanners vuln --severity HIGH,CRITICAL --exit-code 1 \
  --format json --output "$output/vulnerabilities.json" "$image"
