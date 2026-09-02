---
title: "Engineering standards: role split, migration round-trips, test tiers, CI gates"
status: accepted
pinned: false
date: 2026-08-30
---

# 0038. Engineering standards: role split, migration round-trips, test tiers, CI gates

## Decision

Adopted from a sibling repository's proven standards (Kajay), adapted where
LocIntel's architecture demands:

1. **Tenant context is set on every connection open, unconditionally** - the
   org id, or `''` when no tenant resolved. A pooled connection's previous
   borrower can never leak context into the next, by construction rather than
   by trusting the pool's reset behavior. (Transaction-scoped `SET LOCAL`
   would be stronger, but Wolverine's transactional frames own transaction
   lifecycles here; per-request ambient transactions would fight them. The
   always-set session GUC is the deliberate adaptation, proven by a test that
   disables pool reset entirely.)
2. **Owner/app role split, migrations never on api/worker boot.** The
   `migrate` role connects as the database owner, applies every module's
   migrations, provisions the unprivileged `app_user`, and exits; api and
   worker connect only as `app_user`. Superusers bypass RLS unconditionally,
   so an api holding owner credentials would have every tenant-isolation
   policy silently inert. The integration fixture has always used this split;
   now `aspire run` does too.
3. **`Down()` is maintained, not decorative.** `MigrationRoundTripTests`
   applies every module's migrations, reverts to zero, and applies again
   against real PostgreSQL. Never drop a module's schema in `Down()` - it
   holds the migration history table.
4. **Two test tiers with a mechanical wall.** `*.UnitTests` projects are pure
   logic: no mocks, no fakes, no persistence substitutes - enforced by an
   architecture test that reads the project files and rejects forbidden
   packages/references, because it is the reference that makes the wrong test
   cheap to write next. Integration tests (real PostgreSQL, Testcontainers)
   remain the primary proof of functional requirements.
5. **CI gates behind one required status.** `checks` in
   `.github/workflows/checks.yml` fans into architecture+format, unit,
   sharded integration (deterministic partition by test class,
   `tools/run-integration-shard.sh`), contract drift, and frontend jobs.
   Adding a shard or job edits the workflow, never branch protection.
6. **Contract drift is a build failure.** The OpenAPI snapshot and generated
   client are committed; CI regenerates and diffs, so an API change is a
   reviewable diff in the same PR or a red build.
7. **File hygiene, forward-looking.** One primary object per file, named for
   it; supporting types get their own files; no grab-bag files. Existing
   multi-type slice files are grandfathered - apply the rule to new code.

## Why

These rules moved real failure classes (silent RLS bypass, irreversible
migrations, mock-shaped false confidence, contract drift) from review
vigilance into mechanical enforcement in the sibling repo. A template's job
is to make the safe path the cheap path for every fork.

## Consequences

`aspire run` boots a `migrate` resource first; api/worker wait for its
completion. Dev seeding runs as `app_user` under RLS (per-org tenant scopes).
Every new migration must keep `Down()` honest. New pure logic gets unit
tests; everything else stays integration-proven.
