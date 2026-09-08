"""Regression checks for local binds and the security-sensitive CI/deployment wiring."""
from pathlib import Path
import json
import os
import re
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[1]
workflow = (root / '.github/workflows/checks.yml').read_text()
assert '\npermissions:\n  contents: read\n' in workflow
actions = re.findall(r'^\s*(?:-\s+)?uses: (\S+)', workflow, re.MULTILINE)
assert actions and all(re.fullmatch(r'[\w.-]+/[\w./-]+@[a-f0-9]{40}', action) for action in actions)
checkouts = [action for action in actions if action.startswith('actions/checkout@')]
assert len(checkouts) == workflow.count('persist-credentials: false')
assert 'tools/scan-image.sh locintel:ci coverage/image-security' in workflow
assert 'name: image-security' in workflow
assert 'sha256sum --check -' in workflow
assert re.search(r"[a-f0-9]{64}  trivy\.tar\.gz", workflow)

bindings = []
for script in (root / 'tools').glob('*.sh'):
    commands = re.findall(r'^\s*docker run[^\n]*', script.read_text().replace('\\\n', ' '), re.MULTILINE)
    for command in commands:
        for bind in re.findall(r'(?:^|\s)(?:-p|--publish)\s+(\S+)', command):
            bindings.append(bind)
            assert bind.strip('\"\'').startswith('127.0.0.1:'), (script, bind)
assert len(bindings) >= 5

apphost = (root / 'src/LocIntel.AppHost/AppHost.cs').read_text()
for section in ['var apiBuilder =', 'var workerBuilder =']:
    runtime = apphost.split(section, 1)[1].split(';', 1)[0]
    assert '.WithReference(postgres)' not in runtime, section
    assert '.WithEnvironment("ConnectionStrings__locintel", appConnection)' in runtime, section
assert 'Username=app_user;Password=app_user' in apphost

scan = (root / 'tools/scan-image.sh').read_text()
assert '--severity HIGH,CRITICAL --exit-code 1' in scan
assert '--image-src docker' in scan and '--format cyclonedx' in scan
# Exercise the shell contract without Docker/network access: both reports must
# refer to the inspected immutable ID, and a scanner failure must fail the gate.
with tempfile.TemporaryDirectory() as temporary:
    work = Path(temporary)
    fake = '''import json, os, sys
from pathlib import Path
args = sys.argv[1:]
if Path(sys.argv[0]).name == "docker":
    assert args[:2] == ["image", "inspect"]
    fmt = args[-1]
    print("sha256:" + "a" * 64 if fmt == "{{.Id}}" else "[]" if "RepoDigests" in fmt else "DOTNET_VERSION=10.0.11\\nASPNET_VERSION=10.0.11")
elif args == ["--version"]:
    print("Trivy test double")
else:
    assert args[-1] == "sha256:" + "a" * 64
    assert args[args.index("--image-src") + 1] == "docker"
    Path(args[args.index("--output") + 1]).write_text(json.dumps(args))
    if "--exit-code" in args:
        assert args[args.index("--severity") + 1] == "HIGH,CRITICAL"
        sys.exit(int(os.environ["SCAN_EXIT"]))
'''
    for tool in ["docker", "trivy"]:
        path = work / tool
        path.write_text(f"#!{sys.executable}\n" + fake)
        path.chmod(0o700)
    for exit_code in [0, 1]:
        output = work / f"reports-{exit_code}"
        result = subprocess.run(
            ["bash", root / "tools/scan-image.sh", "locintel:ci", output],
            env={**os.environ, "PATH": f"{work}:{os.environ['PATH']}", "SCAN_EXIT": str(exit_code)},
        )
        assert result.returncode == exit_code
        assert (output / "image-id.txt").read_text().strip() == "sha256:" + "a" * 64
        for report in ["sbom.cdx.json", "vulnerabilities.json"]:
            assert json.loads((output / report).read_text())[-1] == "sha256:" + "a" * 64
subprocess.run([sys.executable, root / 'deploy/digitalocean/render.py', '--check'], check=True)
print(f'PASS: {len(actions)} immutable actions, read-only CI token, {len(bindings)} local binds and isolated runtime credentials')
