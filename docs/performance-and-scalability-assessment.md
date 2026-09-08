# Performance and scalability assessment

> Baseline analysis plus implementation follow-up. Historical controlled experiments:
> [capacity validation](capacity-validation.md).
> Current request-rate architecture and qualification: [gateway operational fairness](gateway-fairness.md).

Initial baseline assessed 2026-09-07 at commit `2b2ab21312c38d4fbeaab6afe85a19e752b0f298`;
updated with the subsequent uncommitted implementation and local measurements.
Primary evidence: [performance baseline](perf-baseline.md), [replica scaling](scaling.md),
and [software maturity review](software-maturity-review-details.md), as identified
by the maintainer. Baseline findings below retain their historical context;
the current implementation and qualification ledger is in
[gateway fairness](gateway-fairness.md). Passing short runs do not replace
endurance or independent-host capacity qualification.

The subsequent [architecture hardening](software-maturity-review-details.md#architecture-follow-up-2026-09-07-current-local-work) changes site-write admission and standardizes read transactions. Existing throughput tables remain evidence for their recorded configurations; no new throughput claim follows from these correctness changes.

## 1. Theoretical capacity: what should scale

LocIntel's modular monolith can plausibly serve 1,000 successful business requests
per second with suitable resources. Subsequent short local site-list tests meet
that arrival-rate gate, but production capacity remains unqualified. There is no architectural requirement to split it into microservices
before testing that target. Its database-backed state permits interchangeable API
replicas, and its role flag allows separate deployment of API and worker processes.
Those are prerequisites for horizontal scaling, not a measurement of its efficiency.

A useful capacity model for a fixed workload is:

```
sustainable throughput <= minimum of:
  load generator capacity
  proxy / network capacity
  total API CPU capacity / API CPU seconds per request
  database CPU capacity / database CPU seconds per request
  database I/O and WAL capacity / work per request
  contended resource capacity / visits per request
```

Background work consumes the same database budget. A transaction pool limits
simultaneous database work; it does not make that work disappear. Additional API
replicas help when API resources constrain throughput and downstream resources
have spare capacity. They can reduce throughput when they add scheduling, polling,
connection, or cache-miss work to an already constrained shared host.

For example, **if measured** database CPU demand were 2 ms per successful request,
1,000 requests/sec would consume two database CPU-seconds each second before
background work and operating headroom. That is an illustration, not a measured
sizing recommendation. The recorded 2–3 ms of database execution time per request
must not be substituted for CPU demand: execution duration can include waits,
and concurrent statement durations accumulate beyond wall-clock time.

Concurrency is a separate dimension. In steady state, average in-flight requests
are approximately throughput × **mean** response time. At 1,000 requests/sec and
50 ms mean latency, that is about 50 in flight; at 100 ms, about 100. A 32-client
closed-loop test delivering 650 requests/sec is consistent with roughly 49 ms
mean latency. It cannot independently impose 1,000 arrivals/sec. Do not use p50
or p99 in place of the mean in this calculation.

Define the target as **1,000 successful origin business requests/sec for a specified
mix, tenant distribution, data size, and latency/error budget**. CDN hits, readiness
requests, rejected requests, and accepted asynchronous jobs are different units.
At that rate an hour represents 3.6 million requests. If a selected audit policy
produces one access record per request, that is also 3.6 million records/hour before
other audit and messaging activity; storage and retention must be measured too.

## 2. What the evidence actually establishes

Evidence levels below distinguish recorded results, inspected historical artifacts,
current code/test coverage, and the maintainer's report. None means a fresh passing
run on the reviewed commit unless explicitly stated.

| Area | Available evidence | Defensible conclusion and boundary |
|---|---|---|
| Hours-long soaks at 1, 2, 4 replicas | Maintainer reports roughly 600–700 requests/sec on one machine | Valuable reported endurance evidence. The three supplied documents contain short-run results and leave longer topology/SLO verification open; they do not identify an hours-long run with raw time series, exact command, SHA and resource limits; stable memory, balanced traffic, and a particular bottleneck cannot be independently established here. |
| Short replica benchmark | [Scaling tables](scaling.md): initial 15-second targets, 32 clients, seeded development org | Initial business endpoints span **634–732** at one replica, **596–675** at two, and **509–668** at four. These are endpoint-isolated results, not a mixed 1,000 requests/sec service level. |
| Later direct/pooler comparison | Recorded one-replica direct **440–700**; four direct **400–627**; four transaction-pooled **397–669** requests/sec | Similar throughput with fewer server connections through PgBouncer. Results do not show linear throughput scaling or a universal 600–700 ceiling. No two-replica column is supplied in this later table despite its section title. |
| Connection occupancy | Later four-replica table: direct **140–163**, pooled **73–80** server connections at end of target | Transaction pooling reduced observed server occupancy. End snapshots are not peak occupancy, active transaction counts, or proof of sufficient headroom. |
| Fleet state correctness | Five cases in [FleetTests](../tests/LocIntel.FleetTests/FleetTests.cs), reported passing in scaling notes, including transaction pooling | Shared sessions; sequential idempotent retries; shared sequential quota accounting; sweep claim uniqueness; completion of a 400-row import after the accepting process is killed. These are specific correctness scenarios, not general exactly-once processing, sustained balance, or failover SLOs. |
| Mixed reads and background work | Inspected local `scale-mixed-lookup.trx`: one test passed; 95.8s, 79.4 reads/sec, 284 CPU seconds, 1,193.4 MiB end RSS. [Baseline](perf-baseline.md) records 10,000 import effects, 1,003 purge effects and final drain | Historical mixed completion evidence with a different dataset and in-process TestServer. It predates current paging/index/pooler changes. End RSS is not a leak test. Its 79.4 requests/sec is not inconsistent with small-seed endpoint tests at 600–700. |
| Large-table query design | [ADR 51](decisions/0051-leakproof-keys-under-rls.md) records a million-site tenant plus a 500,000-site neighbor; [ScaleIndexTests](../tests/LocIntel.IntegrationTests/ScaleIndexTests.cs) seeds 2,000 sites and checks index conditions as `app_user` with sequential scans disabled | Historical large-cardinality investigation plus a useful index-eligibility regression guard. The automated check is `EXPLAIN`, not `EXPLAIN ANALYZE`; it does not prove million-row throughput or the planner's normal choice for complete EF queries. |
| Frontend performance | [Baseline](perf-baseline.md): five unthrottled local browser observations per target; built assets and heading timing | Useful smoke/bundle evidence. No loaded SSR capacity, mobile/network budget, interaction tail, or production CDN result. |

The historical mixed-run artifact is under
`tests/LocIntel.IntegrationTests/TestResults/scale-mixed-lookup.trx` locally; it is
not a durable, versioned performance evidence store. Its pass and printed metrics
were inspected, not rerun. Fleet outcomes above come from the committed narrative
and inspected test assertions, not newly executed fleet tests.

The pre-review scaling narrative overstated causality when it said “the api is not
the bottleneck” and “the ceiling is the database.” A shared laptop experiment does
not isolate those alternatives. Its claim that the later numbers were “only higher”
was also unsupported by its own tables: paged sites, for example, go from 732 to
618 requests/sec in the one-replica columns, under runs whose conditions are not
fully matched. Reduced rate-counter duration is useful evidence even without a
corresponding increase in overall throughput. This review corrects those conclusions
in the scaling document while preserving the measured tables.

The maturity review also retains two unreproduced reliability risks: historical
slow successful reads and a test-host shutdown hang. Replays passed, but the causes
were not established as fixed; staging acceptance was provisional. Carry their
trace/dump capture and latency/shutdown checks into deployment and soak acceptance.
Do not reinterpret passing short tests as resolution of those risks.

## 3. Baseline request cost and design strengths

At the initial assessment baseline, the request flow was:
authentication/session validation → API key resolution → guest/tenant context →
rate limits → organization suspension → idempotency → audit wrapper → endpoint.
The business endpoint adds authorization/scope checks, data queries and serialization.
Health probes bypass much of this flow and are not a business-request baseline.

For a typical API-key site list, [API key authentication](../src/LocIntel.Api/ApiKeyAuthenticationMiddleware.cs)
reads the key, the former `SharedRateLimiter` normally
executes an org counter and a key counter, suspension reads the org directory, and
[scope resolution](../src/Modules/LocIntel.Modules.Identity/Access/GrantScopeResolver.cs)
reads the key again plus its grants. The endpoint then counts and fetches a page.
This is a code-derived cost map, not an exact measured SQL count: transaction frames,
caches, tenant settings, audit policy, and the selected endpoint change the total.
The replacement removes both counter updates and adds gateway classification and
local concurrency admission. See [gateway fairness](gateway-fairness.md) for the
current pipeline, verified controls and remaining qualification work.

| Existing optimization | Why it helps | Remaining boundary |
|---|---|---|
| Modular monolith with explicit scope and module ownership | In-process module calls avoid service-to-service hops; shared infrastructure fixes apply broadly | Multiple DbContexts and transactions still cause database work; modularity alone does not reduce it. |
| Shared Npgsql regional data source | Module contexts reuse a data source/pool rather than creating a pool for every context | Messaging and raw connections can use separate pools. Current routing resolves the default region; multi-region capacity is not implemented. See [ModulePersistence](../src/LocIntel.Platform/Data/ModulePersistence.cs). |
| Keyset paging and org-leading derived btree keys | Avoids deep offset walks and makes hot predicates indexable under RLS | Complete query plans, joins, counts, skew and real data distributions still matter. |
| Paged listings plus schedule lookup | [Feed](../src/Modules/LocIntel.Modules.Tenancy/Sites/ListingsFeedEndpoint.cs) defaults to 500 sites, max 2,000; schedules are fetched for that page and grouped once | Schedule/attribute payload per site remains variable; full export requires walking every page. |
| Public list and nearest candidate bounds | [PublicEndpoints](../src/Modules/LocIntel.Modules.Tenancy/Sites/PublicEndpoints.cs) bounds pages and candidate materialization | Bounded nearest search is a heuristic; dense/sparse geographies need result-quality checks as well as timings. |
| Materialized opening windows | Open-now reads avoid expanding recurrence rules per request | Rebuild/roll throughput, freshness and overlapping work become worker obligations. |
| Sixteen rate-counter shards | Reduces contention compared with every org request updating one row | Still writes PostgreSQL on each authenticated request and sums shards; concurrency semantics are approximate. |
| Transaction-scoped tenant state and separate messaging connection | Enables transaction pooling without carrying tenant session state between clients | Preserve RLS and direct messaging advisory-lock semantics; retest through the actual pooler. |
| Durable messages and sweep claims | Committed messages can survive process death; workers avoid duplicate scheduled publishers | A claim before publication leaves a documented recovery gap; a unique claim is not proof the effects completed. |
| Public cache headers and scoped tile cache | Can absorb repeated public reads and expensive low-zoom rendering | Headers do not install a CDN. Cache misses, cold starts and per-replica duplication remain work. |

## 4. Unproven limits and the highest-value investigations

### Historical SQL-limiter finding and remaining authentication overhead

The baseline limiter used synchronous database I/O through `AttemptAcquireCore`.
Captured stacks confirmed blocked request threads; making that I/O asynchronous
alone did not resolve eager read-transaction pool occupancy. The subsequent
read-transaction fixes and controlled experiments are recorded in
[capacity validation](capacity-validation.md). The SQL counter path then remained
a substantial measured cost under concentrated traffic.

After the maintainer clarified that rates are operational fairness, the
replacement moved counters to a native gateway/Redis service and removed the
request-rate billing entitlement. The [current contract](gateway-fairness.md)
describes shared tenant partitions, bounded classification caching, local
concurrency admission, fixed-window attempt counting and fail-open behavior.
Its concurrency, hot-policy, revocation, bypass and outage controls replace the
old sequential SQL quota test. Exact paid-quota semantics are not claimed.

If traces show authentication/grant reads dominate, reuse already-resolved key data
within the request where revocation semantics permit. Prefer this to a fleet cache
of authorization decisions. Human sessions use a different read path and must be
measured separately. The API-key workload also skips access-log publication:
[AccessLogMiddleware](../src/LocIntel.Api/AuditComposition.cs) has no Service case in
its principal switch. Therefore those benchmark numbers do not include a user's
read-audit workload even when that policy is enabled.

### Query and memory growth

[SiteListEndpoints](../src/Modules/LocIntel.Modules.Tenancy/Sites/SiteListEndpoints.cs)
still computes whole-result status counts for a list without a viewport; returning
50 rows does not bound the count. Search/viewport counts cap at 10,000 candidates,
but selective joins can require examining more underlying rows. The open-now
endpoint returns every match without paging. Profile realistic large tenants and
combined scope/search/status/viewport filters using full application SQL and normal
planner settings. Optional/cached totals and paging open-now are candidate changes
when these paths violate their budgets.

[Ingest staging](../src/Modules/LocIntel.Modules.Ingest/StagingService.cs) already
limits live-site lookup to supplied external IDs in chunks, but upload handling
reads the full file, creates parsed records and source rows, and tracks all staged
entities. Preview and commit also materialize batch rows. The 100 MiB upload guard
is not a memory budget or an import throughput promise. Measure concurrent imports
and completion latency; introduce bounded batch processing or streaming if needed.

Low-zoom tile caches are per process, with no configured global size limit or
single-flight fill in [DataLayerEndpoints](../src/LocIntel.Api/DataLayerEndpoints.cs).
ETag comparison happens after rendering/cache lookup, so 304 does not necessarily
save rendering. Test cold concurrent tile requests, many distinct scopes/tiles and
rolling restarts. Org quota and audit policy dictionaries refresh on TTL but do not
evict org entries; tenant churn can grow them independently of a single-org soak.
Add bounds/coalescing only when the measured workload warrants them.

### Background capacity and fairness

API and worker roles both register durable local queues and handlers in
[Program](../src/LocIntel.Api/Program.cs). Only recurring publishers are worker-only.
Adding workers therefore does not prove that API-originated handler work has moved
off the API process. Measure actual per-role execution before promising independent
background scaling; introduce explicit queue placement only if isolation is needed.

[PerOrgSweepService](../src/LocIntel.Platform/Messaging/PerOrgSweepService.cs) loads
org IDs and publishes sequentially under one period claim. More workers do not
parallelize that enumeration. Test enough tenants that fan-out duration matters,
and separately measure effects completed/sec, oldest message age, retries and dead
letters. A queue that drains after traffic stops can still be unstable during
continuous arrivals. If arrivals exceed completion capacity, backlog grows even
while all HTTP requests return successfully.

Test a hot importing tenant beside quiet interactive tenants. Shared pools,
database resources and handlers do not provide demonstrated per-tenant scheduling
fairness. Audit upkeep also has documented exclusive-lock behavior for partition
repair in [production guidance](production.md); ordinary upkeep timing does not
cover a large default-partition repair under load.

## 5. Deployment and load-balancing assessment

The [replica stack](../tools/replica-stack.sh) runs N API **and N worker** native
Release processes, a Node proxy and the generator on the host, and PostgreSQL in
Docker with **two CPUs by default**. It runs in Development with local adapters,
not the Production OCI image. Changing replica count also changes worker polling,
memory use and, in direct mode, per-process pool size. It is a useful local fleet
fixture, not an isolated API scaling experiment.

The [proxy](../tools/replica-proxy.mjs) assigns requests round-robin, uses keep-alive,
buffers request bodies, and retries connection refusal. It is not evidence for
production readiness-based ejection, TLS, HTTP/2 connection behavior, slow-upstream
timeouts, draining, weighted distribution, or cross-host failure handling. Fleet
session tests assert the number of distinct responding replicas, not request counts
or balance under saturation. Equal request counts also need not mean equal work
when request costs differ.

Use the intended production load balancer and fixed per-replica CPU/memory limits
on separate compute capacity. Start with one region, the existing image, a
production PostgreSQL deployment with recovery/HA, transaction pooling for app
queries and direct bounded messaging pools. The repo provides an image build,
smoke script and operational guidance; it does not supply demonstrated hosted
capacity, autoscaling policy, rolling-deploy behavior or database failover SLOs.
No platform migration or particular cloud purchase is justified by these results.

Budget **all pools**, including rollout surge and direct messaging. In the local
PgBouncer setup, an 80-server-connection pool plus eight process messaging pools
of up to 10 each is a potential 160 before other direct users, against a server
configured for 100. Observed 73–80 occupancy means those maxima were not all reached;
it does not make the worst-case budget safe. Measure and explicitly cap aggregate
pooler and direct use, leaving operational reserve. Do not simply raise connections
until exhaustion disappears. Transaction pooling reuses server connections between
transactions and needs compatible session-state usage; prepared-statement support
requires configuration. [PgBouncer features](https://www.pgbouncer.org/features.html).

Cross-host session portability also needs shared cookie/data-protection keys and
consistent configuration. [AuthenticationHosting](../src/LocIntel.Api/AuthenticationHosting.cs)
requires `DataProtection:KeyPath` in Production and sets a common application name;
the deployment must make that path a truly shared, persistent key store. Same-user processes on one laptop do not validate an
independent container's keyring. Include fresh replicas and key rotation in the
real topology's session test.

Deploy the console as static assets and the public app as a separate SSR runtime.
The [public API client](../web/apps/public/src/api.ts) fetches both sites and `/me`
for a locator render, and forwards cookies. A page view can therefore create
multiple API requests. Cache public content with host/tenant/query separation and
validated cookie/session bypass rules; do not indiscriminately cache personalized
HTML. Measure CDN-hit, miss, and direct-origin capacities separately.

## 6. Baseline measurement gaps and their disposition

The original [load runner](../tools/load-baseline.mjs) remains appropriate for quick
relative comparisons. The following baseline gaps motivated the new k6 harness;
arrival scheduling, body/error/latency assertions, realistic Host handling, per-minute
results, Npgsql/runtime metrics and process instance IDs are now implemented.
Representative business data and independent compute remain open:

- Closed-loop workers wait for each response and run endpoints sequentially. Use
  an arrival-rate workload for 1,000 requests/sec, with one counted business request
  per iteration and enough generator capacity. Include dropped/unscheduled arrivals
  in the verdict. [k6's model explanation](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/open-vs-closed/)
  and [constant-arrival-rate executor](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/constant-arrival-rate/)
  document this distinction.
- The runner has no explicit per-request deadline, throughput/latency assertions,
  response-content checks, or failure exit for HTTP errors. It records only
  successful-request latencies and retains/sorts every sample in memory. Long
  durations increase generator memory and can hide degradation in one aggregate.
- Warmup occurs after the duration deadline is created, slightly shortening the
  measured load window. Five requests are not enough to characterize cold versus
  warm behavior across caches and replicas.
- All routes receive the same bearer token; `members` predictably returns 403.
  Separate expected authorization tests from successful business throughput, and
  run guest host resolution/cookies without a service credential.
- “Full fleet” and “unpaged” labels are stale: feed/public endpoints are paged now.
  The mixed harness still requests `offset=950`, which no longer exercises a deep
  sites page. Follow returned cursors and assert distinct expected content.
- SQL statistics reset is global to the benchmark database. Background traffic
  contaminates attribution, and classification by query text is approximate.
  Collect an idle baseline, normal request traces and database wait samples.
  Connection snapshots and summed SQL durations cannot identify CPU saturation.
- The baseline lacked explicit Npgsql diagnostics: Program originally
  registered ASP.NET, HttpClient and Wolverine telemetry, but not the Npgsql meter
  or activity source. Capture per-pool use/waits alongside runtime and database
  signals. [Npgsql diagnostics](https://www.npgsql.org/doc/diagnostics/overview.html)
  and [metrics](https://www.npgsql.org/doc/diagnostics/metrics.html) describe available
  instrumentation. Give every process a unique instance identity; MachineName
  alone merged same-host replicas in the original OTLP resource configuration.
  The implementation now includes Npgsql/runtime meters and machine-plus-PID identity.

Keep the lightweight runner for local comparisons. Use an established arrival-rate
load tool for acceptance rather than growing it into a custom benchmark platform.
Retain SHA, build/image digest, seed recipe and cardinalities, command, quotas,
resource limits, pool settings, adapter/audit configuration and timestamped output
with every run. Archive failures as well as successes.

## 7. Recommended sequence and decision gates

### First: establish an attributable short-run baseline

Instrument the current tree; run 5–10 minute steps at 250, 500, 750, 1,000 and
1,250 offered business requests/sec, stopping escalation on sustained errors or
queue/resource growth. These rates/durations are proposed investigation settings.
Warm up separately. Repeat comparable runs at least three times and vary their
order to reduce host/temperature/cache bias.

| Experiment | Hold constant | Prediction and decision |
|---|---|---|
| Move generator to independent compute; compare direct API and intended LB | App/DB capacity, workload, pools | If throughput rises with no app change, local generator/proxy/shared resources constrained the previous result. Direct tests diagnose overhead; only LB runs count as deployment acceptance. |
| API replicas 1 → 2 → 4 on additional fixed-size compute | DB resources, worker count, audit policy, total offered mix | API saturation hypothesis predicts higher sustainable capacity with lower per-instance CPU/queue pressure. Flat throughput requires examining downstream waits and generator capacity, not declaring an API success. |
| DB resources 2 → 4 CPUs, then I/O change only if indicated | API/worker resources and traffic | DB CPU hypothesis predicts reduced saturation and increased throughput; a lock bottleneck predicts persistent lock waits despite spare CPU. Separate CPU from storage experiments. |
| One hot org/key versus many orgs/keys | Total arrival rate and comparable data/query cost | Counter/tenant contention predicts worse waits for concentrated traffic. Concurrent quota assertions must pass as well. |
| Historical SQL-limiter ablation (retired) | One API instance, same workload | Completed before replacement; see [capacity validation](capacity-validation.md). The old `RateLimits:Shared` switch no longer exists; do not use it to interpret current runs. |
| Worker load off / steady / burst | API and DB resources | Rising interactive tails with queue growth identifies shared-resource contention; measure completion, not just enqueue acceptance. |

Record per-replica successes, errors, in-flight requests, response histograms,
CPU, throttling, RSS/heap/GC and ThreadPool queues; generator CPU/event-loop/network;
LB connections/retries; PostgreSQL CPU, disk latency, WAL, locks/waits, buffer reads,
long transactions and vacuum; PgBouncer waiting clients and all Npgsql pools;
message age, arrival/completion rate, retries and dead letters.

### Second: prove representative data and business behavior

Run the agreed mix against small, 10,000-, 100,000- and, if a product requirement,
1,000,000-site tenants with realistic schedules, attributes and geographic skew.
Use quiet neighbors plus hot tenants, human sessions, API keys and real guest
requests. Include cursor traversal, rare/common searches, subtree filters, cold
map tiles, full paged exports and concurrent imports. Verify data and scope
correctness throughout. Add/check only the scenarios required by the product;
these sizes are test dimensions, not advertised support limits.

Tune only the dominant measured cost: counter path, repeated request-local reads,
counts, batch materialization, cache misses, pool waits or queue placement.
Keep RLS, validation, durable delivery and authorization checks. The measured
SQL-counter cost and operational-fairness decision justified the native shared
gateway limiter now implemented. Add read replicas only if measured reads can
tolerate lag and be separated from authorization/write requirements; consider
regional partitioning only when measured capacity or residency demands it.
Neither database sharding nor microservices is currently justified.

### Third: run the long soak and failure tests with acceptance criteria

Agree the final product SLO before execution. A concrete **proposed starting contract**:

| Dimension | Proposed acceptance |
|---|---|
| Sustained capacity | At least 1,000 successful origin business requests/sec over a 4-hour steady phase; provision offered rate accordingly; no dropped generator arrivals. Report every minute and per route, not just the whole-run average. |
| Interactive latency | Per interactive route p95 ≤ 100 ms and p99 ≤ 250 ms. Exports/import completion have separately agreed budgets; do not hide them in the interactive aggregate. These are proposed targets, not current guarantees. |
| Errors/correctness | Unexpected request failures < 0.1%; no integrity/tenant-isolation errors; expected quota tests separate; no acceptance pass while shared limiter fails open or the queue silently loses work. |
| Even distribution | For identical healthy replicas and a uniform-cost workload, each receives within ±10% of its expected `1/N` share over steady one-minute windows, with no persistently overloaded outlier. Mixed traffic also compares cost and tails per replica. |
| Stability | No continuing post-warmup heap/RSS/pool-wait/backlog-age growth; CPU/I/O headroom recorded; cleanup and vacuum keep pace; effects complete and final drain has no unexplained dead letters. |
| Failure/recovery | At target arrivals, remove one API and one worker separately; measure detection, ejection, loss/retry behavior, remaining capacity and recovery. Test pooler/DB failover and rolling surge separately against agreed outage/recovery budgets. |

For four replicas, losing one increases each survivor's average share from 250 to
about 333 requests/sec at a 1,000 total target. For two replicas, the survivor must
handle all 1,000. Choose replica count from measured normal and degraded capacity,
not from “four sounds scalable.” Do not require perfect balance while a replica
is warming or draining; define those windows and still report their user impact.

## 8. Current position and next decision

**The template now has a working portable gateway reference and short local
1000-arrivals/sec proof for a small API-key site-list workload. It does not yet
have a general 1000-requests/sec production capacity claim.** The heavier mixed
workload demonstrates why route mix and dataset size must be part of the claim.

| Evidence | What it establishes | What it does not establish |
|---|---|---|
| 1, 2 and 4 APIs, two gateways, admission 128; 120 seconds at 1000 arrivals/sec after target-rate warmup | Each run completed 120,000 valid site-list requests with zero errors/drops and passing replica distribution and limiter-health checks; p95/p99 were 12/24, 19/41 and 36/85ms | Hours of stability, larger datasets, human read-audit cost, linear capacity gain on additional hosts |
| Sustained noisy/quiet tenant test | At 1000 noisy arrivals/sec plus 10 quiet/sec, all 1,201 quiet requests succeeded (p95 9.1ms); 119,881 noisy requests were correctly throttled; no limiter errors/fail-open | Fairness for every operation cost or distributed denial-of-service protection |
| 1,006 sites; six read routes at equal shares; two APIs | 1000/sec **failed**: 74,086 valid successes, 9,515 HTTP 503s, 36,400 dropped arrivals and 19 fail-open decisions over 120 seconds | The small-list pass cannot be generalized to this workload |
| Matching mixed workload at 500 and 250 arrivals/sec, 120 seconds each | 500/sec failed latency (p95/p99 233/384ms; seven 503s). 250/sec passed with 30,001 successes, zero errors/drops, p95/p99 33/95ms and no limiter failures | A capacity boundary precise to a single rate, repeatability or multi-hour stability |
| Same mixed workload at 250/sec for ten minutes | 150,001 valid successes; zero errors/drops or limiter failures; p95/p99 15/21ms; balanced full minutes; sampled API memory 1.276–1.365 and 1.065–1.109 GiB | Hours-long retained-heap stability, concurrent write/import pressure, loaded failover or external scale efficiency |
| Direct and transaction-PgBouncer fleet suites | Session interchangeability, idempotency, sweep leases and durable completion after killing the accepting API all pass | Zero-downtime failover or a safe worst-case aggregate connection budget |
| Gateway and public frontend controls | Eight gateway groups pass; real SSR tenant-host routing, revocation, private ingress, policy changes and dependency failures are exercised | A complete production frontend/CDN/TLS topology or every provider integration |
| Full integration suites | 311 tests pass with one intentional scale skip in completed replays | The earlier post-test shutdown hang is not established as fixed |

The detailed [gateway ledger](gateway-fairness.md) retains failed experiments,
actual settings, diagnostic observations and evidence locations. Defaults are
operational starting values: the reference uses 128 local request slots while
standalone mode keeps 32. Historical plan-rate configuration must be exported
before upgrading, because subsequent billing synchronization can remove obsolete
plan-owned entries. Business resource quotas remain in the application.

The next deployment decision should use an independent load generator and
explicit per-service CPU, memory and database-connection budgets. Measure the
intended route/authentication mix at representative data sizes, then increase
API compute while holding database and worker resources constant. Run the next
hours-long soak at a configuration with measured headroom, including background
work and separately reported failure/recovery windows. Keep microservice
extraction, fleet-wide authorization caches and broad timeout increases off the
critical path until measurements justify them.

The final gateway control replay also closes a casing mismatch in the private
identity route: the gateway now denies uppercase/mixed-case variants before
classification. All eight control groups pass after that change. Normal reference
policy was restored after benchmark overrides. The detailed ledger identifies
the exact measured bootstrap versus the subsequent private-route correction.


### Priorities after this change

1. **Qualify the intended deployment before claiming scale.** Run the
   [external procedure](capacity-validation.md#external-qualification-handoff)
   with fixed resource budgets and a representative dataset. The local small-list
   pass proves a narrow capability; the mixed failures identify a real remaining
   limit without isolating how much belongs to the shared host.
2. **Remove measured redundant request work.** API-key service identities still
   pass through guest middleware that checks only the cookie principal. Trace and
   skip the guest-only host/session work for service principals, retaining guest,
   user-session and revocation regressions. The observed extra host lookup makes
   this a concrete candidate; measure its effect before claiming a gain.
3. **Profile expensive mixed reads.** Capture plans and buffer/I/O statistics for
   the search count and large feed/public result paths on representative data.
   Evaluate whether clients need an exact count and the current page size. Change
   the query/API contract only with product agreement and correctness checks;
   do not infer that a new index or cache will fix the measured tails.
4. **Finish deployment budgets and recovery.** Bound aggregate primary, pooler
   and direct messaging connections including rollout surge. Establish Redis/RLS
   recovery alerts and the consequences of the chosen fail-open policy. Validate
   TLS, trusted client-IP handling (including SSR), shared keys, worker drain and
   loaded replica loss in the fork's real topology.

These are separate experiments or deployment gates. Combining them into another
large optimization change would lose attribution. Business quotas, authorization,
RLS and durable completion remain requirements throughout.
