#!/usr/bin/env python3
"""LocIntel fork initializer (ADR 36): renames the template to YOUR product.

Usage: python3 tools/init.py Acme
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

if len(sys.argv) != 2 or not re.fullmatch(r"[A-Z][A-Za-z0-9]+", sys.argv[1]):
    sys.exit("usage: init.py <PascalCaseName>   e.g. init.py Acme")

name = sys.argv[1]
lower = name.lower()
upper = name.upper()
root = pathlib.Path(__file__).resolve().parent.parent
SKIP_DIRS = {".git", "node_modules", "bin", "obj", "dist", ".tanstack", ".aspire"}
TEXT_SUFFIXES = {".cs", ".csproj", ".slnx", ".json", ".yaml", ".yml", ".md", ".ts",
                 ".tsx", ".css", ".html", ".py", ".sh"}

def eligible(path: pathlib.Path) -> bool:
    return not any(part in SKIP_DIRS for part in path.parts)

# 1. contents
for path in root.rglob("*"):
    if path.is_file() and eligible(path) and path.suffix in TEXT_SUFFIXES:
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

print(f"""
renamed template -> {name}
next steps:
  1. review: git diff --stat
  2. integration tests (needs Docker): dotnet test tests/{name}.IntegrationTests
  3. cd web && pnpm install && pnpm typecheck
  4. update workos-emulate.config.yaml seed org/user for your product
  5. commit; delete this script if you like ceremony-free repos""")
