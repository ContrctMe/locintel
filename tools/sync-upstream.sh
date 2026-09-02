#!/usr/bin/env bash
# Pull the template forward into this fork.
#
# The fork was cut with tools/init.py (a one-way rename), so upstream commits
# cannot merge directly: every file they touch exists here under the product
# name. This script keeps a parallel branch, template-renamed, holding each
# upstream snapshot AFTER the same rename and format pass, each snapshot
# parented on the previous one. Merging that branch gives git a real merge
# base, so conflicts appear only where both sides changed the same lines -
# never for the rename itself.
#
#   tools/sync-upstream.sh [<template repo path or url>] [<ref>]
#
# tools/init.py performs the first-run bootstrap: it adds the source repo as
# the "template" remote and creates template-renamed at the fork's init
# commit. If you forked by hand, do that once yourself:
#   git remote add template <template repo path or url>
#   git branch template-renamed <the commit that renamed the template>
# Every run after that: fetch, rename, snapshot, merge. Resolve conflicts in
# upstream's favour, then re-seat the fork's code on whatever upstream lifted.
set -euo pipefail

repo=$(git rev-parse --show-toplevel)
cd "$repo"
product=$(basename "$(ls "$repo"/*.slnx | head -1)" .slnx)
template=${1:-}
ref=${2:-template/main}

if ! git remote get-url template >/dev/null 2>&1; then
  [ -n "$template" ] || { echo "no 'template' remote; pass the template repo path or url" >&2; exit 1; }
  git remote add template "$template"
fi
git rev-parse --verify -q template-renamed >/dev/null \
  || { echo "no template-renamed branch; create it from the fork's init commit first (see header)" >&2; exit 1; }
[ -z "$(git status --porcelain)" ] || { echo "working tree must be clean" >&2; exit 1; }

git fetch -q template
upstream=$(git rev-parse --short "$ref")
last=$(git log -1 --format=%s template-renamed | grep -oE '[0-9a-f]{7,}' | head -1 || true)
if [ "$last" = "$upstream" ]; then
  echo "template-renamed already holds $upstream; nothing to sync"
  exit 0
fi

wt=$(mktemp -d "${TMPDIR:-/tmp}/template-renamed.XXXXXX")
trap 'git worktree remove --force "$wt" >/dev/null 2>&1 || true' EXIT
git worktree add -q --detach "$wt" "$ref"
(
  cd "$wt"
  python3 tools/init.py "$product" >/dev/null
  dotnet csharpier format . >/dev/null
  git add -A
  tree=$(git write-tree)
  commit=$(git commit-tree "$tree" -p template-renamed \
    -m "template $ref $upstream, renamed to $product (init.py + csharpier)")
  git branch -f template-renamed "$commit"
)
echo "template-renamed -> $(git rev-parse --short template-renamed) (upstream $upstream)"

echo "merging template-renamed into $(git branch --show-current)..."
if git merge --no-ff --no-commit template-renamed; then
  echo "clean merge; review, run the suites, then commit"
else
  echo
  echo "conflicts:"; git diff --name-only --diff-filter=U | sed 's/^/  /'
  echo
  echo "resolve in upstream's favour (git checkout --theirs -- <file>), re-seat fork code on"
  echo "anything upstream lifted, run every suite, then commit the merge."
fi
