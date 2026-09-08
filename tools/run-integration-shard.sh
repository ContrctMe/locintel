#!/usr/bin/env bash
# Deterministic integration-test sharding (ADR 38): partition by TEST CLASS,
# taken from the runner's own sorted list, so a class stays whole in one
# process (one Testcontainers fixture per class, never split across shards).
# Usage: tools/run-integration-shard.sh <index-from-1> <count>
# CONFIGURATION defaults to Debug for local use; CI sets Release. The run is
# --no-build, so it must match what was built.
# HANG_DUMP_TYPE=full opts into heap diagnostics; normal CI keeps smaller mini dumps.
set -euo pipefail

INDEX="${1:?shard index (1-based) required}"
COUNT="${2:?shard count required}"
CONFIGURATION="${CONFIGURATION:-Debug}"
PROJECT="tests/LocIntel.IntegrationTests"

# Theory cases arrive as Class.Method(arg: "value"), and an argument can
# contain dots and slashes (an open-redirect test's "https://evil.com" did),
# so arguments must be cut BEFORE the trailing .Method - stripping the last
# dot-segment first mangles the class name and puts "/" into the filter,
# which MSBuild rejects as an invalid property.
classes=$(
  dotnet test "$PROJECT" -c "$CONFIGURATION" --no-build --list-tests 2>/dev/null |
    grep -E '^[[:space:]]+LocIntel\.IntegrationTests\.' |
    sed -E 's/^[[:space:]]+//; s/\(.*$//; s/\.[^.]+$//' |
    sort -u
)

# A class name is letters, digits, underscores and dots - nothing else. If a
# future test shape defeats the parse again, fail here with the offending
# line rather than emitting a filter that breaks the build far from the cause.
if bad=$(printf '%s\n' "$classes" | grep -vE '^[A-Za-z0-9_.]+$' | head -3) && [ -n "$bad" ]; then
  echo "shard: could not parse test class names from --list-tests:" >&2
  printf '  %s\n' "$bad" >&2
  exit 1
fi

filter=""
i=0
while IFS= read -r class; do
  if [ $((i % COUNT)) -eq $((INDEX - 1)) ]; then
    # trailing dot: contains-match without prefix collisions between classes
    part="FullyQualifiedName~${class}."
    filter="${filter:+$filter|}$part"
  fi
  i=$((i + 1))
done <<<"$classes"

if [ -z "$filter" ]; then
  echo "shard $INDEX/$COUNT: no test classes assigned"
  exit 0
fi

echo "shard $INDEX/$COUNT: $(echo "$filter" | tr '|' '\n' | wc -l | tr -d ' ') classes"
# COVERAGE=1 collects line/branch coverage (coverlet, Cobertura) into TestResults/
# for the CI report; opt-in so a local shard stays fast. Report-only: there is no
# floor until a few weeks of numbers say where one belongs (maturity review).
set --
if [ "${COVERAGE:-0}" = "1" ]; then
  set -- --collect:"XPlat Code Coverage" --settings coverage.runsettings
fi
results="$PROJECT/TestResults/shard-$INDEX-$$"
dotnet test "$PROJECT" -c "$CONFIGURATION" --no-build --filter "$filter" "$@" \
  --blame-hang-timeout 5m --blame-hang-dump-type "${HANG_DUMP_TYPE:-mini}" \
  --results-directory "$results" \
  --logger "trx;LogFileName=integration-shard-$INDEX.trx"
if [ "${COVERAGE:-0}" = "1" ] && ! find "$results" -name coverage.cobertura.xml -print -quit | grep -q .; then
  echo "shard $INDEX/$COUNT: coverage collector produced no Cobertura report" >&2
  exit 1
fi
