#!/usr/bin/env bash
# Repeatable local operating-envelope baseline. It reports observations, not
# pass/fail latency budgets: compare runs on the same host and investigate drift.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

cd "$root"
LOCINTEL_SCALE_BASELINE=1 Logging__LogLevel__Default=Warning dotnet test tests/LocIntel.IntegrationTests \
  -c Release --filter 'FullyQualifiedName~BackgroundSweepTests.Scale_baseline' \
  --logger 'console;verbosity=detailed'

cd "$root/web"
pnpm build
