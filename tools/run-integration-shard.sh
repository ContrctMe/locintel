#!/usr/bin/env bash
# Deterministic integration-test sharding (ADR 38): partition by TEST CLASS,
# taken from the runner's own sorted list, so a class stays whole in one
# process (one Testcontainers fixture per class, never split across shards).
# Usage: tools/run-integration-shard.sh <index-from-1> <count>
# CONFIGURATION defaults to Debug for local use; CI sets Release. The run is
# --no-build, so it must match what was built.
set -euo pipefail

INDEX="${1:?shard index (1-based) required}"
COUNT="${2:?shard count required}"
CONFIGURATION="${CONFIGURATION:-Debug}"
PROJECT="tests/LocIntel.IntegrationTests"

classes=$(
  dotnet test "$PROJECT" -c "$CONFIGURATION" --no-build --list-tests 2>/dev/null |
    grep -E '^\s+LocIntel\.IntegrationTests\.' |
    sed -E 's/^\s+//; s/\.[^.]+$//' |
    sort -u
)

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
exec dotnet test "$PROJECT" -c "$CONFIGURATION" --no-build --filter "$filter" \
  --logger "trx;LogFileName=integration-shard-$INDEX.trx"
