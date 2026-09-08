# 52. Rate limits are counted in Postgres, not in the process

Date: 2026-09-06
Status: superseded for request rate limiting by [ADR 54](0054-gateway-operational-fairness.md)
Pinned: false

## Context

The text below records the historical design. Request rates are now classified
as operational fairness, not sold quotas. Idempotency and sweep leases still use
Postgres; the request counter implementation is being retired under ADR 54.

ADR 30 partitions rate limiting by principal: a per-org quota from the
metered entitlement, then a per-user (or per-API-key) limit, then a guest or
IP limit. All three ran on ASP.NET's in-memory fixed-window limiter, which is
correct for one api process and wrong for two: every replica keeps its own
count, so a fleet of N replicas gives every org and every user N times the
quota it is sold, and the test that proves "the plan reaches the limiter"
keeps passing on each replica alone.

The production topology (docs/production.md) has always said "1+ replicas"
for the api role. Before the multi-instance work of 2026-09 nothing ran two,
so the quota drift was invisible.

## Decision

The partitions that name a quota someone is sold - org, user, API key -
count in one place: a `platform.rate_windows` row per (partition, minute),
incremented by one upsert per request that returns the window's count. The
row is the lock; there is no leader, no cache to invalidate, and a replica
that starts mid-window sees the same number as the others. Guest and IP
partitions stay in process memory: they are abuse control on unauthenticated
traffic, not a number a customer can exceed by being routed to a second
replica, and a database round trip per anonymous request is a cost the
public locator should not pay.

The shared limiter fails open. If the counter cannot be reached, the request
proceeds and the failure is logged once per window: the database being down
already fails every request that needs it, and the limiter must never be the
reason a healthy request is refused. Spent windows are deleted by the hourly
idempotency cleanup sweep, ten minutes after they start.

`RateLimits:Shared=false` restores the in-memory limiter for a single-process
deployment that wants to skip the round trip. It is the only configuration
under which two api replicas are not a supported topology.

## Consequences

- One indexed upsert per authenticated request, roughly a millisecond on
  the same host. The first shape was one row per partition and minute, and
  the fleet bench found every request of an org waiting on that row's lock,
  15 ms each at 32 concurrent - the database's whole ceiling. A window is
  now sixteen rows; a request bumps one and reads the sum.
- The counter is exact across replicas within a window; the in-memory
  guest limiter is per replica, and the production doc says so.
- Two other per-process caches remain and are documented rather than
  shared: the plan-limit site count (sixty seconds; a burst can exceed the
  plan by one window's worth across replicas) and the cluster-tile cache
  (sixty seconds; only a cost).
- The fleet suite (`tools/replica-stack.sh`) proves the quota holds across
  two and four replicas, and is the place any future shared state is proven.
