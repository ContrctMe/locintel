---
title: "Fork model"
status: accepted
pinned: false
date: 2026-08-29
---

# 0036. Fork model

## Decision

GitHub template repo with an init script (renames namespaces, sets product name, generates keys, removes samples) and a module generator (schema, DbContext, migration history, Wolverine registration, arch-test registration, test fixtures).

**Amended 2026-09-02: there is now a best-effort upstream merge story.** The
rename is still one-way - `tools/init.py` rewrites every namespace, so an
upstream commit can never merge into a fork directly; git sees the whole tree
as unrelated. `tools/sync-upstream.sh` closes that gap without reversing the
rename: it replays each upstream snapshot through the SAME rename onto a
parallel `template-renamed` branch, each snapshot parented on the last, so
merging that branch gives git a real merge base. Conflicts then appear only
where both sides changed the same lines - never for the rename itself.

`init.py` bootstraps it: the source repo is recorded as the `template` remote,
the rename is committed, and `template-renamed` is created at that commit
(which must be the post-rename commit, or the merge base is an unrenamed tree
and the whole rename returns as a conflict).

## Why

The generator matters because every module needs its own schema and migration history (ADR 17). .claude/ and CLAUDE.md are product surface forks inherit.

Merging was originally declared out of scope because a renamed fork has no
shared history to merge against. That reasoning was right about the mechanism
and wrong about the conclusion: the shared history can be MANUFACTURED by
replaying upstream through the same deterministic rename. A fork that skips
syncing is unaffected; one that wants platform fixes now has a path.

## Consequences

- Platform fixes reach forks that run the sync; forks that never run it are
  exactly where they were.
- The sync is assisted, not automatic. Conflicts are resolved in upstream's
  favour and fork code is re-seated on whatever upstream lifted - a real
  review, not a button. A first sync touching ~a third of changed files is
  normal.
- `template-renamed` must never be rebased or squashed: it is the merge base.
