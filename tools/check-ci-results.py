"""Fail the required aggregate check unless every dependency succeeded."""

import json
import os
import sys

results = json.loads(os.environ["NEEDS_JSON"])
failed = [name for name, job in results.items() if job.get("result") != "success"]
if not results or failed:
    sys.exit("Required jobs did not succeed: " + (", ".join(failed) or "no results"))
print("All required jobs succeeded")
