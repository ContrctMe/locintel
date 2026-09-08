# Performance baseline and operating envelope

**Last updated:** 2026-09-04

These are local observations, not production capacity promises: Apple-Silicon
laptop, Release build, quiet logging, Wolverine dynamic code generation, and
PostgreSQL in Docker. Use them to compare paths and detect drift on the same
class of host. A fork must load-test its own infrastructure, adapters, data
shape, and service-level objectives before launch.

## Current mixed-workload method and results

`tools/scale-baseline.sh` now runs 16 closed-loop clients across six routes
(first/deep site page, full listings feed, audit changes, public list, and public
near-sort). Reads continue for **at least 60 seconds and through all background
phases**. One in-process TestServer host also executes the durable handlers;
PostgreSQL 17 runs in Docker (7.75 GiB VM limit). Host: Apple M1 Pro, 10 logical
CPUs, 16 GiB RAM, macOS 26.6.2; .NET SDK 10.0.102 / runtime 10.0.2, Release.
This measures shared-process contention, not a
separate API/worker deployment or network capacity.

The fixture starts with 1,003 organizations, 1,000 sites and schedules in Org A,
and 10,000 audit changes. It stages and commits 10,000 new sites into Org A:
the read dataset grows to **11,000 sites**, still with 1,000 schedules. It then
publishes 1,003 tenant purge messages, requiring every expired file's tombstone
and actual local object deletion, invokes global partition upkeep, and checks
durable drain with no dead letters. The latest harness also checks drain after
all reads stop. Partition lifecycle correctness is covered separately; this
timing measures upkeep of already provisioned partitions, not a disaster-recovery
exercise. Imports use the real staging service and HTTP commit; CSV upload and
malware scanning are covered separately, not included in these timings.

Benchmark-only per-user, guest, and organization limits are raised to 1,000,000
requests/minute before measurement. Production limits are unchanged. Each worker
cycles equally through routes; this is a synthetic mix, not observed customer
traffic. Latencies include response-body consumption. The load generator and
application share the measured process CPU/RSS; memory is end-of-traffic RSS,
not peak memory. Concurrent database resource observations are snapshots only.

The pre-lookup, bounded-pool run (`scale-mixed-bounded-pools.trx`) completed in
92.3 seconds with 6,873 successful reads (74.5 requests/s), zero HTTP errors,
and clean shutdown logs. It used 296.2 test-host CPU seconds and ended at
820.7 MiB RSS. Full import business completion took 70.93s from commit start;
commit acceptance alone took 13.88s. Purge publication took 6.77s versus 10.93s
to verify all effects. Audit upkeep took 1.66s. This run predates the final
post-traffic drain assertion and schedule-lookup optimization; it is a
comparison baseline, not final-tree evidence.

| Route | p50 | p95 | p99 |
| --- | ---: | ---: | ---: |
| Full listings feed | 444 ms | 1,653 ms | 2,344 ms |
| First site page | 120 ms | 293 ms | 497 ms |
| Deep site page | 118 ms | 260 ms | 428 ms |
| Audit changes | 126 ms | 289 ms | 943 ms |
| Public list | 85 ms | 297 ms | 545 ms |
| Public near-sort | 85 ms | 348 ms | 647 ms |

The feed previously scanned all schedules per site; the optimized run below measures a
standard per-site lookup. Earlier unbounded-pool runs completed business work
but logged connection exhaustion during shutdown; their green test status was
not treated as clean operational evidence. Bounding omitted pool sizes to 20
removed those errors in the comparison run. Replica-wide pool budgets still
require deployment-specific sizing.

### Optimized current-tree run

`scale-mixed-lookup.trx` passed with clean teardown: 95.8s, 7,605 successful
reads, 79.4 requests/s, zero HTTP errors. All 10,000 imports completed 71.26s
after commit started (12.36s acceptance), all 1,003 purge effects completed in
13.78s (9.91s publication), partition upkeep took 1.29s, and final post-traffic
drain took 10.9ms with no dead letters. Test-host CPU was 284s; end RSS was
1,193.4 MiB. The feed's p50/p95/p99 were 365/1,359/1,874ms, versus
444/1,653/2,344ms before the lookup. Other route p95s were 279–323ms.
This single before/after pair suggests the expected feed improvement; differences
in memory, other routes, and background timings are not statistically established
regressions or gains. No memory ceiling can be inferred from end RSS.

**Tested local envelope:** one host, 16 closed-loop read clients, 1,003 orgs,
fleet growth from 1,000 to 11,000 sites with 1,000 schedules, a 10,000-row import,
and 1,003 one-byte local-object purges. This is a completed synthetic scenario,
not a supported production request rate. Full feeds still materialize the fleet;
CSV still materializes the file and staged rows. Larger files/fleets, sustained
multi-hour runs, separate replicas, cloud object sizes/latency, stricter SLOs,
or higher concurrency require another benchmark and pool-budget review. Use
bounded exports/streaming only when those measurements justify the added cost.

## Historical exercised shapes

These shapes were exercised by the earlier cardinality or endpoint-isolated
benchmarks below, not by a sustained mixed workload on the current tree.
They do not establish worker completion capacity, full import throughput, or a
supported deployment envelope. The completed current mixed-workload evidence is
recorded above and supersedes these historical measurements for that scenario.

| Dimension | Initial envelope | Evidence and boundary |
| --- | ---: | --- |
| Active customer organizations | 1,000 | 1,003-org enumeration and durable fan-out baseline |
| Sites in one organization | 1,000 | Paged, full-feed, public-list, and near-sort baselines |
| Schedules in one organization | 1,000 | One schedule per site in the full listings feed |
| CSV ingest | 10,000 rows / about 0.5 MB | Parse and database staging baseline; the 100 MB upload guard is not an ingest promise |
| Console site page | 50 default, 200 maximum | Server-side count, filtering, and offset pagination |
| Audit query | 50 default, 500 maximum | Bounded newest-first query over 10,000 rows |
| Concurrent baseline traffic | 16 clients | Standalone HTTP load baseline below; not a deployment quota |
| Worker replicas | Multiple | Period leases ensure one logical publisher; replicas do not multiply fan-out volume |

The current scenario above exercises fleet growth to 11,000 sites and 1,003
organizations. Re-baseline before claiming larger shapes, higher concurrency,
or deployment capacity; neither table establishes production support limits.

## Repeatable cardinality baseline (2026-09-04)

Run `tools/scale-baseline.sh`. It creates an isolated PostgreSQL container,
seeds 1,000 sites and schedules, 10,000 audit rows, 10,000 CSV rows, and 1,000
additional organizations, reports observations, and builds both frontends. It
asserts shape and counts but deliberately does not fail on wall-clock variance.

| Target | Median / elapsed | Response size |
| --- | ---: | ---: |
| `/api/sites?limit=50` | 21.3 ms | 18,945 bytes |
| `/api/sites?limit=50&offset=950` | 21.1 ms | 18,949 bytes |
| `/api/listings/feed` (1,000 sites + schedules) | 110.6 ms | 471,866 bytes |
| `/api/audit/changes?limit=500` over 10,000 rows | 17.1 ms | 111,937 bytes |
| `/public/sites` (1,000 sites) | 18.7 ms | 188,781 bytes |
| `/public/sites?near=…` (in-memory Haversine sort) | 13.6 ms | 187,861 bytes |
| CSV parse, 10,000 rows | 26.7 ms | 397,819-byte input |
| CSV stage, 10,000 rows against 1,000 live sites | 2,607.5 ms | n/a |
| Worker organization enumeration, 1,003 orgs | 5.7 ms | n/a |
| Worker durable fan-out, 1,003 messages | 1,020.1 ms | n/a |

Frontend output from the same run:

| Artifact | Minified | Gzip |
| --- | ---: | ---: |
| Console entry | 260.15 kB | 82.51 kB |
| Console shared source chunk | 137.34 kB | 44.19 kB |
| Largest console feature chunk (sites) | 16.21 kB | 4.74 kB |
| Public browser entry | 319.19 kB | 102.63 kB |
| Public Leaflet chunk | 148.82 kB | 43.39 kB |
| Public SSR server | 228.80 kB | 52.49 kB |

The full listings feed is the heaviest read, but it is a connector-polled export
rather than a hot interactive path. CSV staging and worker publication were
measured separately; neither result proves that the requested site changes or
purge effects completed. No capacity or completion-time conclusion should be
drawn from those two historical timings. The current mixed workload above
separately verifies import and purge completion.

## Built frontend loading observation (2026-09-04)

Run `E2E_LOADING_BASELINE=1 tools/e2e-stack.sh --project=chromium`.
The current built console and public SSR app were served locally; five fresh
Chromium contexts per target reused authentication state, not HTTP cache.
The dataset was the fresh development seed, not the high-cardinality backend
benchmark. There was no CPU/network throttling or concurrent backend load.

| Target | Visible heading median / maximum | TTFB median | Transfer bytes at observation | Resources |
| --- | ---: | ---: | ---: | ---: |
| Console sites | 231 / 848 ms | 2.0 ms | 156,812 | 15 |
| Public SSR locator | 529 / 640 ms | 16.9 ms | 100,398 | 5 |

These are five observations, not a reliable tail-latency distribution. The
heading check does not prove full interaction readiness or completion of every
lazy asset. No page JavaScript exceptions occurred. Both builds and the probe
exited successfully. Transfer size is the browser's navigation/resource timing
sum at observation, not a total application download-size claim.

## Historical standalone HTTP load baseline (2026-08-30)

1. Seed about 1,000 open sites with coordinates in one org.
2. Raise org and process request limits so throttling is not the measurement.
3. Run the API standalone in Release with quiet logs.
4. Run `node tools/load-baseline.mjs <base> <locintel_key> [seconds] [concurrency]`.

The zero-dependency runner warms each target and reports throughput and latency.

| Target | rps | p50 | p95 | p99 |
| --- | ---: | ---: | ---: | ---: |
| `/healthz` (pipeline floor) | 2,296 | 6.3 ms | 11.0 ms | 16.6 ms |
| `/api/sites?limit=50` | 646 | 23.5 ms | 34.3 ms | 40.3 ms |
| `/api/sites?q=…` | 607 | 25.2 ms | 35.3 ms | 43.2 ms |
| `/api/sites/{id}` | 921 | 16.7 ms | 22.4 ms | 29.9 ms |
| `/api/listings/feed` | 264 | 59.8 ms | 80.5 ms | 98.8 ms |
| `/public/sites` | 505 | 30.0 ms | 43.8 ms | 49.8 ms |
| `/public/sites?near=` | 507 | 30.1 ms | 43.2 ms | 47.8 ms |

The first load run found and fixed two real bugs: service principals were
throttled as anonymous IPs, and the org quota cache refreshed without tenant
context, making the configured entitlement inert. Regression tests cover both.
