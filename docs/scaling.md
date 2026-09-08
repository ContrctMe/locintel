# Scaling out: what two and four replicas prove

> Historical measurements and fleet scenarios. The [2026-09-07 assessment](performance-and-scalability-assessment.md)
> qualifies their scope: flat throughput on a shared host does not isolate the
> bottleneck, and the fleet suite does not establish sustained even distribution.

The production topology (`docs/production.md`) runs the api and worker roles
as replicas behind a load balancer. This page is the evidence that they can:
which per-process state was made fleet-safe, the suite that proves it, and
the load table at one, two and four replicas on one host.

## What had to change (2026-09-06, ADR 52)

The next table records the historical SQL-counter design. [ADR 54](decisions/0054-gateway-operational-fairness.md)
now replaces request-rate counters and the quota cache with gateway operational
fairness. Its current implementation and checks are in [gateway fairness](gateway-fairness.md).

| State | Before | As tested on 2026-09-06 |
|---|---|---|
| Org / user / API-key rate limit | In-memory fixed window per process: N replicas gave N times the quota | One `platform.rate_windows` row per (partition, minute), upserted per request; fails open |
| Org quota cache | Five-minute per-process cache; a quota change reached one replica | Fifteen seconds; every replica learns a change by expiry |
| Local object-store upload tickets | An in-memory dictionary on the replica that issued the ticket | Signed with the master key, redeemable on any replica (the dev adapter only; cloud adapters were already presigned URLs) |
| Sessions, idempotency keys, sweep leases, the outbox | Already in Postgres | Unchanged, now proven across processes |
| Plan-limit site count, cluster-tile cache | Per process, sixty seconds | Per process on purpose, listed in `docs/production.md` |

## The fleet suite

`tools/replica-stack.sh N` boots one Postgres, the migrate role once, N api
and N worker processes from the same build, and a round-robin proxy, then
runs `tests/LocIntel.FleetTests` through the proxy:

| Case | Proves |
|---|---|
| One session is answered by every replica | Sessions are database state; the proxy spreads a cookie's requests and every replica answers it |
| An idempotency key holds across replicas | The same key sent to different replicas creates one site; a mid-flight retry is a 409 |
| Each sweep runs once per period across workers | With N workers ticking, `platform.sweep_runs` holds one claim per (sweep, period) |
| Messages survive the death of the replica that took them | A 400-row ingest batch is committed, the replica that answered is killed with SIGKILL, and the survivors apply every row from the durable queue |

Every case skips when `LOCINTEL_FLEET_URL` is unset, so `dotnet test` over
the solution never needs the stack.

The former SQL quota case was retired with ADR 54. The gateway suite verifies
tenant fairness separately, including multiple credentials and gateway replicas.

## Load at one, two and four replicas

`tools/replica-stack.sh N --bench 15 32` signs in as the seeded owner, mints
an API key and runs `tools/load-baseline.mjs` through the proxy: 15 seconds
per target at 32 concurrent requests. One laptop, one Postgres, every replica
on the same cores: the numbers are relative and the ceiling is the host,
not the design. Fill the table from the run's output.

Run on 2026-09-06 (Apple Silicon laptop, Postgres 17 in Docker, seeded dev
org). Requests per second, then p50 / p99 in milliseconds.

| Target | 1 replica | 2 replicas | 4 replicas |
|---|---|---|---|
| healthz (pipeline floor) | 9,012 · 3.2 / 8.8 | 8,705 · 3.3 / 9.4 | 7,859 · 3.5 / 11.6 |
| sites paged (limit 50) | 732 · 38.5 / 130.7 | 636 · 41.9 / 167.5 | 509 · 54.4 / 199.6 |
| sites search | 692 · 39.3 / 138.1 | 630 · 43.3 / 158.8 | 593 · 46.9 / 153.4 |
| site detail | 724 · 35.7 / 152.5 | 675 · 38.1 / 162.6 | 635 · 43.1 / 152.0 |
| listings feed | 678 · 40.0 / 141.8 | 642 · 41.0 / 159.6 | 624 · 44.2 / 147.7 |
| public sites (paged) | 717 · 37.0 / 141.6 | 673 · 37.6 / 167.5 | 668 · 39.9 / 147.7 |
| public sites near | 634 · 43.3 / 144.0 | 596 · 45.8 / 157.0 | 551 · 51.0 / 163.5 |

Zero errors on every row at every size. (`members` is left out: it is a
humans-only endpoint and the bench signs in with an API key, so every call
is a correct 403.)

These results show flat or lower throughput as replicas are added on one host.
They do not isolate API CPU, database CPU or waits, generator/proxy overhead,
or shared-host contention. The fleet suite separately exercises shared-state
correctness. Additional independent compute and resource measurements are needed
to establish which component limits capacity and whether API replicas increase it.

The first four-replica run failed on something else: eight processes
against the Postgres image's default hundred connections exhausted the
server (`53300: sorry, too many clients already`) and the shared limiter
failed open as designed. The stack now sizes `max_connections` and gives
each process its share of the pool - the budget `docs/production.md` asks
operators for, found the hard way.

Throughput scaling must be tested with controlled per-replica resources and
an independently capable generator. Flat throughput alone does not identify the
component that should be optimized next.

## What the database does per request

The same bench with Postgres held to two CPUs (`--pg-cpus 2`, the default)
and `pg_stat_statements` loaded, so each target reports the statements the
server ran per request and, ranked by execution time, what the server's
cores actually went on. The first run of this bench corrected the working
theory: connection churn was not the cost.

| Statement (paged list, per request) | count | cost |
|---|---|---|
| `DISCARD ALL` (Npgsql resetting a pooled connection) | ~8 | 8 µs each |
| `SET LOCAL app.org_id` (the tenant, ADR 53) | ~5 | 40 µs each |
| the list's own count and page | 2 | 1.3 ms together |
| the rate counters (ADR 52), first shape | 2 | **15 ms each** |

The rate counter was the ceiling. Its first shape was one row per
partition and minute, so every request of an org waited on the previous
request's row lock: at 32 concurrent requests that was 15 ms of the
database's time per request, 116 seconds across the fifteen-second run,
against a fifth of a millisecond for the real queries. A window is now
sixteen rows; a request bumps one shard and reads the sum. Database time
per request fell from 31 ms to 2-3 ms. The churn everyone suspects
(connection resets, the tenant variable) is microseconds and stays.

## Two and four replicas, with the fix, direct and through PgBouncer

Requests per second, then p50 / p99 in milliseconds, then server
connections in use at the end of the run.

| Target | 1 replica, direct | 4 replicas, direct | 4 replicas, PgBouncer transaction |
|---|---|---|---|
| sites paged (limit 50) | 618 · 54 / 96 · 57 | 534 · 55 / 218 · 140 | 522 · 58 / 172 · 73 |
| sites search | 568 · 66 / 101 · 57 | 477 · 65 / 190 · 161 | 531 · 60 / 118 · 75 |
| site detail | 700 · 33 / 90 · 57 | 592 · 52 / 108 · 161 | 669 · 43 / 102 · 77 |
| listings feed | 592 · 61 / 96 · 57 | 580 · 59 / 103 · 161 | 565 · 55 / 129 · 79 |
| public sites (paged) | 656 · 39 / 91 · 57 | 627 · 50 / 97 · 162 | 633 · 48 / 105 · 79 |
| public sites near | 440 · 80 / 151 · 57 | 400 · 85 / 147 · 163 | 397 · 84 / 151 · 80 |

Zero errors on every row. Throughput remains broadly similar across these
configurations; the tables do not establish an overall speedup from the fix
or isolate the remaining bottleneck. The occupancy column shows a clearer change:
through the bouncer the four replicas
hold 73 to 80 server connections where direct connections hold 140 to 163,
with every process asking for a pool of a hundred. That is transaction
pooling doing what it is for, and the fleet suite passes five of five
through it, including the replica killed mid-batch.

## PgBouncer

`tools/replica-stack.sh N --pgbouncer` runs the api and worker replicas
through a PgBouncer container (`edoburu/pgbouncer`) in transaction mode,
with Postgres kept at its default hundred connections and every process
asking for a pool of a hundred - the configuration a bare server refused
with `53300`. The message store takes its own direct connection
(`ConnectionStrings:locintel-messaging`, ADR 53) and the bouncer tracks
prepared statements (`MAX_PREPARED_STATEMENTS`).

Transaction mode is the mode that matters: a server connection serves a
client for one transaction and someone else for the next, so the server
sees in-flight transactions rather than every replica's whole pool. It was
not possible before ADR 53 - the tenant variable was session state, and
`--pgbouncer transaction` failed three of five fleet cases with work
attributed to the wrong org. With the variable transaction-scoped and the
message store direct, the same run passes five of five. Session mode
(`--pgbouncer session`) still works and only queues.

The dev host has the same pooler behind a flag: `LOCINTEL_PGBOUNCER=1
aspire run` routes api and worker through a PgBouncer container in
transaction mode while the migrate role keeps its direct owner connection.
