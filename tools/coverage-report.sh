#!/usr/bin/env bash
# Coverage by test tier and by module, report-only. CI downloads unit reports
# under <input>/unit and integration reports under <input>/integration.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-$root/coverage}"
input="${2:-$root/tests/LocIntel.CoverageInput}"
python3 "$root/tools/check-coverage-input.py" "$input/integration"
dotnet tool restore >/dev/null

report() {
  local label=$1 source=$2 target=$3 files
  files=$(find "$source" -name coverage.cobertura.xml -type f | sort | tr '\n' ';')
  [ -n "$files" ] || { echo "$label coverage: no Cobertura reports under $source" >&2; exit 1; }
  dotnet reportgenerator "-reports:${files%;}" "-targetdir:$target" "-reporttypes:Html;MarkdownSummaryGithub" \
    "-assemblyfilters:+LocIntel.*;-LocIntel.*Tests" "-classfilters:-*.Migrations.*;-*Internal.Generated*" >/dev/null
  echo "# $label coverage"
  echo
  cat "$target/SummaryGithub.md"
}

report Unit "$input/unit" "$out/unit"
report Integration "$input/integration" "$out/integration"
report Combined "$input" "$out/combined"
