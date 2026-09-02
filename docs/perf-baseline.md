# Performance baseline

First measured numbers (2026-08-30), against a local instance - which makes
them **relative costs, never capacity planning**: Apple-Silicon laptop,
Release build, `Logging=Warning`, Wolverine codegen still Dynamic, client
and server sharing one machine, Testcontainers-style Postgres in Docker.
Use them to compare endpoints with each other and to catch regressions in
the same setup, not to size production.

## Method (repeatable)

1. Seed a fleet: ~1,000 open sites in one org (coordinates set).
2. Raise the org's `api.requests_per_minute` entitlement and the
   `RateLimits:*PerMinute` config far above the load, so the limiter is not
   what you measure.
3. Run the API standalone (Release, quiet logs) against the same database.
4. `node tools/load-baseline.mjs <base> <locintel_key> [seconds] [concurrency]`
   - zero dependencies, warms up each target, reports rps and p50/95/99.

## Numbers (1,000-site org, 8s per target, concurrency 16)

| Target | rps | p50 | p95 | p99 |
|---|---|---|---|---|
| `/healthz` (pipeline floor) | 2,296 | 6.3ms | 11.0ms | 16.6ms |
| `/api/sites?limit=50` (paged) | 646 | 23.5ms | 34.3ms | 40.3ms |
| `/api/sites?q=…` (search) | 607 | 25.2ms | 35.3ms | 43.2ms |
| `/api/sites/{id}` (detail) | 921 | 16.7ms | 22.4ms | 29.9ms |
| `/api/listings/feed` (full fleet) | 264 | 59.8ms | 80.5ms | 98.8ms |
| `/public/sites` (unpaged, 1,000 rows) | 505 | 30.0ms | 43.8ms | 49.8ms |
| `/public/sites?near=` (+ haversine) | 507 | 30.1ms | 43.2ms | 47.8ms |

## Readings

- **The documented ceilings hold at 1,000 sites.** The unpaged public list
  and in-memory haversine cost ~30ms p50 - the maturity review's "revisit
  at 10k" stays the honest line; nothing needs optimizing at template scale.
- **The feed is the heaviest read** (fleet + schedules), as expected; it is
  a connector-polled endpoint, not a hot path.
- **`Principal.User`-guarded endpoints (members, roles) reject API keys**
  regardless of grants - by design (they are human-flavored surfaces); a
  fork that wants service access there changes the guard deliberately.

## What the first run found (why baselines exist)

Two real bugs fell out before a single useful number was recorded, both
fixed with regression tests:

1. **Service principals throttled as anonymous IPs** - API-key requests
   carried no claims and no guest cookie, so they landed in the per-IP
   guest bucket (60/min) and skipped the org quota entirely.
2. **The org quota entitlement was silently inert** - the limit cache
   refreshed in a scope with no tenant context, RLS hid the org's row, and
   the catalog default (600/min) was cached for every org; on top of that,
   partition limiters are created once, so a hot org would never have seen
   an upgraded quota. Fixed by tenant-scoping the refresh and putting the
   limit in the partition key; the refresh also logs loudly now instead of
   swallowing failures.
