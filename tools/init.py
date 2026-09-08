#!/usr/bin/env python3
"""LocIntel fork initializer (ADR 36): renames the template to YOUR product.

Usage: python3 tools/init.py Acme [--snapshot]

--snapshot renames and nothing else: no verification pass, no git bootstrap.
tools/sync-upstream.sh uses it to replay an upstream snapshot through the
same rename inside a worktree, where committing or moving refs would corrupt
the very branch the sync is building.
Renames LocIntel -> Acme across solution, projects, namespaces, config keys,
schemas stay as-is (they are yours to evolve), and the web workspace scopes.
Run BEFORE the first migration is applied anywhere durable. One-way: there is
no upstream-merge story after forking - this is stated, not implied.
"""
import pathlib
import re
import shutil
import subprocess
import sys

args = sys.argv[1:]
snapshot = "--snapshot" in args
args = [a for a in args if a != "--snapshot"]
if len(args) != 1 or not re.fullmatch(r"[A-Z][A-Za-z0-9]+", args[0]):
    sys.exit("usage: init.py <PascalCaseName> [--snapshot]   e.g. init.py Acme")

name = args[0]
lower = name.lower()
upper = name.upper()
root = pathlib.Path(__file__).resolve().parent.parent
# whether the fork's tree was clean BEFORE the rename decides if this script
# may commit it: sweeping someone's in-progress work into the init commit
# would be rude and hard to unpick
clean_before = not subprocess.run(["git", "status", "--porcelain"], cwd=root,
                                  capture_output=True, text=True).stdout.strip()
SKIP_DIRS = {".git", "node_modules", "bin", "obj", "dist", ".tanstack", ".aspire"}
TEXT_SUFFIXES = {".cs", ".csproj", ".slnx", ".json", ".yaml", ".yml", ".md", ".ts",
                 ".tsx", ".css", ".html", ".py", ".sh", ".mjs", ".js", ".runsettings",
                 ".tf", ".hcl", ".example", ".webmanifest", ".props", ".targets", ".sql"}

def eligible(path: pathlib.Path) -> bool:
    return not any(part in SKIP_DIRS for part in path.parts)

# 1. contents
for path in root.rglob("*"):
    if path.is_file() and eligible(path) and (path.suffix in TEXT_SUFFIXES or path.name in {".gitignore", ".csharpierignore"}):
        text = path.read_text(errors="ignore")
        # all three case variants: UPPER first-class, because env vars like
        # LOCINTEL_API survived a LocIntel/locintel-only rename and broke the
        # AppHost, both web apps, and the production guide in a real fork
        replaced = (text
                    .replace("LocIntel", name)
                    .replace("LOCINTEL", upper)
                    .replace("locintel", lower))
        if replaced != text:
            path.write_text(replaced)

# 2. file and directory names (deepest first)
renames = sorted(
    (p for p in root.rglob("*LocIntel*") if eligible(p)),
    key=lambda p: len(p.parts), reverse=True)
for path in renames:
    path.rename(path.with_name(path.name.replace("LocIntel", name)))

# 3. remove template-only bits
for sample in []:  # add paths here if the fork should drop sample slices
    shutil.rmtree(root / sample, ignore_errors=True)

def run(label, *command):
    print(f"  {label} ...", flush=True)
    result = subprocess.run(command, cwd=root, capture_output=True, text=True)
    if result.returncode != 0:
        print(f"  ! {label} failed - run it yourself and fix before committing")
        print((result.stdout + result.stderr)[-1500:])
    return result.returncode == 0

# 3. leave the fork in a committable state, not a broken one. Renaming the
# product namespace changes where it sorts against Microsoft.*, so csharpier's
# using order shifts across the tree - a fork discovered this as a CI format
# failure on its very first commit.
if snapshot:
    print(f"renamed template -> {name} (snapshot: no verification, no git bootstrap)")
    raise SystemExit(0)

print("verifying the rename:")
run("format", "dotnet", "csharpier", "format", ".")
built = run("build", "dotnet", "build", f"{name}.slnx")
if built:
    # fast suites only: these need no Docker, so a missing daemon cannot
    # make a rename look broken. Integration tests are printed below.
    run("architecture tests", "dotnet", "test", "tests/" + name + ".ArchitectureTests")
    run("unit tests", "dotnet", "test", "tests/" + name + ".Platform.UnitTests")

# renames show up as delete+untracked, so `git diff --stat` undercounts wildly;
# the porcelain status is the honest "what did this touch" number
status = subprocess.run(["git", "status", "--porcelain"], cwd=root,
                        capture_output=True, text=True)
print(f"\n{len(status.stdout.splitlines())} paths touched")

# Bootstrap the sync story (ADR 36). The fork is a one-way rename, so upstream
# commits can never merge directly - tools/sync-upstream.sh replays each
# upstream snapshot through this same rename onto a parallel branch, and that
# branch must START at the commit that renamed the template. So: remember
# where this repo came from, commit the rename, and mark it.
def git(*args):
    return subprocess.run(["git", *args], cwd=root, capture_output=True, text=True)

origin = git("remote", "get-url", "origin").stdout.strip()
if origin and not git("remote", "get-url", "template").stdout.strip():
    git("remote", "add", "template", origin)
    print(f"template remote -> {origin}")

if clean_before and git("rev-parse", "--verify", "-q", "HEAD").returncode == 0:
    git("add", "-A")
    if git("commit", "-m", f"Initialize {name} from the LocIntel template").returncode == 0:
        git("branch", "-f", "template-renamed", "HEAD")
        print(f"committed the rename as {git('rev-parse', '--short', 'HEAD').stdout.strip()} "
              "and marked template-renamed there")
        print("  (undo with: git reset --soft HEAD~1 && git branch -D template-renamed)")
else:
    print("tree was dirty before the rename, so it was NOT committed; after you commit,")
    print("  run: git branch template-renamed HEAD   (sync-upstream.sh needs it)")

print(f"""
renamed template -> {name}
next steps:
  1. review: git diff --stat
  2. integration tests (needs Docker): dotnet test tests/{name}.IntegrationTests
  3. cd web && pnpm install && pnpm typecheck
  4. update workos-emulate.config.yaml seed org/user for your product
  5. pull the template forward later with: tools/sync-upstream.sh""")
