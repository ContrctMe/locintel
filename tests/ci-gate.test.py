"""Exercise the actual gate command and its workflow dependency wiring."""

import json
import os
from pathlib import Path
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
workflow = (root / ".github/workflows/checks.yml").read_text()
gate = workflow.split("\n  checks:\n", 1)[1]
jobs = set(re.findall(r"^  (\w+):$", workflow.split("\njobs:\n", 1)[1], re.MULTILINE)) - {"checks"}
dependencies = set(re.search(r"needs: \[(.*?)\]", gate)[1].split(", "))
assert dependencies == jobs, (dependencies, jobs)
assert "    if: ${{ always() }}" in gate
assert "NEEDS_JSON: ${{ toJSON(needs) }}" in gate
assert "python3 tools/check-ci-results.py" in gate


def run(results):
    return subprocess.run(
        [sys.executable, root / "tools/check-ci-results.py"],
        env={**os.environ, "NEEDS_JSON": json.dumps(results)},
        capture_output=True,
        text=True,
    ).returncode


successful = {job: {"result": "success"} for job in jobs}
assert run(successful) == 0
assert run({}) != 0
for job in jobs:
    for status in ("failure", "cancelled", "skipped", "unknown", None):
        assert run({**successful, job: {"result": status}}) != 0, (job, status)
print(f"PASS: workflow wiring, success, empty results, and {len(jobs) * 5} negative cases")
