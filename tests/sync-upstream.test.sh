#!/usr/bin/env bash
# Round-three item 15: the second sync is the one that broke.
#
# Forks a throwaway clone, syncs it twice against a template that moves in
# between, and asserts each merge touches only genuinely-changed files. The
# bug this pins parented each snapshot on the fresh init commit rather than
# the previous snapshot, so the merge base fell back to the original fork
# point and every renamed file conflicted - invisible on a first sync,
# obvious on a second.
set -euo pipefail

template=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
work=$(mktemp -d "${TMPDIR:-/tmp}/sync-test.XXXXXX")
trap 'rm -rf "$work"' EXIT

fail() { echo "FAIL: $*" >&2; exit 1; }

# An upstream the test can move without touching the real repo.
upstream="$work/upstream"
git clone -q "$template" "$upstream"
# CI checks out a detached PR merge; local runs may start on a feature branch.
# The fixture must advance main, which the sync command reads by default.
git -C "$upstream" switch -q -C main
git -C "$upstream" config user.email t@example.com
git -C "$upstream" config user.name Test
# The working-tree copies of the tooling, so the test exercises what is being
# changed rather than what is committed. Identical to HEAD is the normal case
# once a change lands, and `git commit` exits non-zero on an empty commit -
# which under `set -e` would fail the test for the wrong reason.
cp "$template/tools/init.py" "$template/tools/sync-upstream.sh" "$upstream/tools/"
git -C "$upstream" add -A
git -C "$upstream" diff --cached --quiet \
  || git -C "$upstream" commit -q -m "tooling under test"

# Fork it.
fork="$work/fork"
git clone -q "$upstream" "$fork"
git -C "$fork" config user.email t@example.com
git -C "$fork" config user.name Test
(cd "$fork" && python3 tools/init.py Acme >/dev/null)

git -C "$fork" rev-parse --verify -q template-renamed >/dev/null \
  || fail "init.py did not create template-renamed"
[ -z "$(git -C "$fork" status --porcelain)" ] || fail "init.py left the tree dirty"

# Operational files must be renamed too; these escaped the original suffix allow-list.
for file in coverage.runsettings tools/fleet-bench.mjs tools/gateway.test.mjs tools/capacity.k6.js deploy/digitalocean/main.tf deploy/digitalocean/compat.tfvars.example deploy/digitalocean/compat.tftest.hcl web/apps/console/public/manifest.webmanifest .gitignore .csharpierignore; do
  grep -Ei 'locintel' "$fork/$file" && fail "unrenamed product reference in $file"
done
grep -Fq '[Acme.*]*' "$fork/coverage.runsettings" || fail "coverage must measure fork assemblies"

# The fork does its own work, in a file upstream also owns.
echo "// fork-owned" >> "$fork/docs/production.md"
git -C "$fork" add -A && git -C "$fork" commit -q -m "fork: local change"

sync_once() {
  local label=$1 line=$2
  echo "$line" >> "$upstream/docs/runbook.md"
  git -C "$upstream" add -A
  git -C "$upstream" commit -q -m "template: $label"

  (cd "$fork" && tools/sync-upstream.sh >/dev/null 2>&1) || true
  local conflicts
  conflicts=$(git -C "$fork" diff --name-only --diff-filter=U | wc -l | tr -d ' ')
  local touched
  touched=$(git -C "$fork" diff --cached --name-only | wc -l | tr -d ' ')
  echo "  $label: $touched file(s) touched, $conflicts conflict(s)"
  [ "$touched" -ge 1 ] || fail "$label delivered no staged change"

  # The property under test: a sync touches the files upstream changed, not
  # the whole renamed tree. A wrong merge base shows up as dozens.
  [ "$touched" -le 5 ] || fail "$label touched $touched files - wrong merge base?"
  [ "$conflicts" -le 1 ] || fail "$label produced $conflicts conflicts - wrong merge base?"

  git -C "$fork" checkout --theirs -- . >/dev/null 2>&1 || true
  git -C "$fork" add -A
  git -C "$fork" commit -q -m "sync: $label" || true
}

echo "syncing twice:"
sync_once "first upstream change" "First line from upstream."
sync_once "second upstream change" "Second line from upstream."

# The chain must be snapshot -> snapshot, never snapshot -> init commit.
parent=$(git -C "$fork" rev-parse template-renamed^)
subject=$(git -C "$fork" log -1 --format=%s "$parent")
case "$subject" in
  template\ *) ;;
  *) fail "second snapshot's parent is '$subject', expected the previous snapshot" ;;
esac

grep -q "Second line from upstream." "$fork/docs/runbook.md" \
  || fail "the second sync did not deliver upstream's change"
grep -q "fork-owned" "$fork/docs/production.md" \
  || fail "the sync lost the fork's own change"
grep -rq "LocIntel" "$fork/src/Acme.Api/Program.cs" \
  && fail "the rename did not hold through the sync"

before=$(git -C "$fork" rev-parse template-renamed)
if (cd "$fork" && bash tools/sync-upstream.sh "" template/main~1 > "$work/downgrade.log" 2>&1); then
  fail "sync accepted a snapshot older than the current upstream baseline"
fi
grep -q 'non-descendant upstream snapshot' "$work/downgrade.log" || fail "downgrade failed for the wrong reason"
[ "$before" = "$(git -C "$fork" rev-parse template-renamed)" ] || fail "rejected downgrade moved the snapshot branch"
[ -z "$(git -C "$fork" status --porcelain)" ] || fail "rejected downgrade dirtied the fork"
echo "PASS: two syncs, correct merge base, complete rename, downgrade refused, fork intact"
