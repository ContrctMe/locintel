#!/usr/bin/env bash
# Negative control: a failed real stack run must retain diagnostics and fail.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
probe_dir=$(mktemp -d "${TMPDIR:-/tmp}/locintel-e2e-artifact-check.XXXXXX")
if "$root/tools/e2e-stack.sh" --project=diagnostic-probe > "$probe_dir/run.log" 2>&1; then
  echo "FAIL: invalid browser project unexpectedly succeeded; $probe_dir/run.log" >&2
  exit 1
fi
if ! grep -Fq 'Project(s) "diagnostic-probe" not found' "$probe_dir/run.log"; then
  echo "FAIL: stack failed before the intended browser error; $probe_dir/run.log" >&2
  exit 1
fi
diagnostics=$(sed -n 's/^Failure diagnostics: //p' "$probe_dir/run.log" | tail -1)
for log in api.log console.log public.log postgres.log; do
  if [ ! -s "$diagnostics/$log" ]; then
    echo "FAIL: missing $log from failed run; $probe_dir/run.log" >&2
    exit 1
  fi
done
cp -R "$diagnostics" "$probe_dir/stack-logs"
echo "PASS: failed stack retained all four logs; evidence: $probe_dir"
