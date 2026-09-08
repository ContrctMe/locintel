# Capacity validation and bottleneck experiments

Work begun 2026-09-07 following the [assessment](performance-and-scalability-assessment.md).
The local fixture is a diagnostic environment, not independently provisioned API
compute or production deployment acceptance. Cross-host load generation and the
selected production topology still require an accessible staging environment.

The SQL-counter experiments below are historical evidence from earlier working
tree states. [ADR 54](decisions/0054-gateway-operational-fairness.md) replaces that
implementation; use the [gateway reference](gateway-fairness.md) for current
fairness qualification. The standalone harness below still measures the API,
but no longer reproduces the removed SQL limiter or its `Shared=false` control.

## Run the standalone API harness

Install the official native k6 executable and optionally Microsoft's `dotnet-counters`.
This investigation uses k6 2.2.0 and dotnet-counters 10.0.731102, installed in `/tmp`;
they are not application dependencies. Docker and the existing .NET SDK are required.

```bash
# A controlled hot-tenant sites-list run; fail if offered traffic cannot complete
# within the proposed error/latency/balance budgets.
RATE=1000 CAPACITY_SECONDS=60 tools/replica-stack.sh 1 --workers 1 --capacity

# Hold workers and DB quota constant while changing API process count.
RATE=1000 CAPACITY_SECONDS=60 tools/replica-stack.sh 2 --workers 1 --capacity
RATE=1000 CAPACITY_SECONDS=60 tools/replica-stack.sh 4 --workers 1 --capacity

# Separate database experiment: same APIs/workers, change only DB CPU quota.
RATE=1000 CAPACITY_SECONDS=60 tools/replica-stack.sh 1 --workers 1 --capacity --pg-cpus 4

# Seed through application writes; all derived keys, RLS and audit behavior apply.
SEED_SITES=1000 RATE=500 CAPACITY_SECONDS=300 \
  ROUTES=sites,search,detail,feed,public,near \
  tools/replica-stack.sh 2 --workers 1 --capacity

# Connection-pooler correctness (includes process death and durable completion).
tools/replica-stack.sh 2 --pgbouncer transaction

# Benchmark acceptance controls; good responses pass, 503/invalid/slow responses fail.
node tools/capacity.test.mjs
python3 tools/capacity-report.py --check
```

Set `K6` and `DOTNET_COUNTERS` to executable paths if they are not on PATH.
`DOTNET_COUNTERS=disabled` gives an observer-off comparison. `VUS` defaults to 256:
these are preallocated arrival slots, not an imposed concurrency target. Exhausting
them produces dropped arrivals and fails the test. `FLEET_AUTO_PREPARE=100` tests native Npgsql automatic preparation (default remains 0).
`WARMUP_RATE` and `WARMUP_SECONDS` control a separate warmup (defaults 50 and 10).
`P95_MS=100` and `P99_MS=250`
are proposed interactive budgets, not established service guarantees. `AUTH=user`
uses the seeded owner's cookie for private routes; the default is an API key.
Public/near routes use guest host resolution without a bearer token. `SEED_SITES`
adds that many synthetic sites to the development seed, with deterministic names
and coordinates; it does not seed schedules, realistic attributes or extra tenants.

The local setup deliberately lifts development business quotas for seeding.
Historical runs also lifted request allowances while retaining SQL counter execution;
that counter is now removed. Do not use its credential/seed preparer
against production. Its fixed Docker container names are for an isolated developer
stack, so do not run multiple replica-stack invocations at once.

The k6 script can run independently on another machine:

```bash
FIXTURE=/secure/path/staging-fixture.local.json RATE=1000 CAPACITY_SECONDS=300 \
  SUMMARY=/secure/results/summary.json \
  k6 run --out json=/secure/results/samples.json.gz tools/capacity.k6.js
```

Supply a private fixture with `base`, a dedicated test `token`, `siteId`, optional
user `cookie`, guest `guestHost`, and expected `instances`. Match the tested schema
and credentials to that staging dataset; do not place secrets in Git or command
arguments. The `guestHost` value supplies the actual HTTP Host header; the gateway does not
trust client-supplied forwarded host metadata. On the real public host test the
actual DNS/routing.
Production response-instance headers may be unavailable; obtain per-instance counts
from the load balancer/telemetry instead of claiming the k6 balance check ran.

## Evidence and verdict

Every local run prints its evidence directory. It contains the source SHA, dirty
state and source file hashes, selected workload/resource settings, dataset counts,
k6 version, warmup and measured summaries, compressed timestamped samples,
per-minute result bins, API/worker logs, SQL statistics, process and database
resource snapshots, and optional per-API runtime/pool metrics. The private fixture
is removed when the stack exits. Preserve result directories before temporary-file
cleanup; they are local test data, not a production telemetry destination.

Each arrival schedules one business GET. Response bodies must match the expected
shape and contain data (detail must match the selected ID). Non-200, invalid bodies,
transport failures, dropped arrivals, insufficient successes, per-route latency
breaches and aggregate replica imbalance produce failure. Expected negative HTTP
tests are separate. An observed fail-open counter/pool-exhaustion log also fails
local acceptance, even if HTTP thresholds passed. A printed result is not a pass:
use the exit code and threshold details in `summary.json`.

`minutes.json` reports UTC completion-minute counts, errors, dropped arrivals,
per-route latency and per-replica successes. First/last bins may be partial;
inspect steady full minutes separately. Aggregate ±10% replica balance is asserted
by k6; per-minute balance and memory/backlog trends require reviewing the timeline.
No four-hour, degraded-capacity, fairness, or memory-stability claim follows from a
short passing run. SQL duration is not CPU time; database and process snapshots
are samples, and active diagnostic tools consume some host capacity.

## Root causes isolated so far

1. **Synchronous shared limiter acquisition blocks request threads.** At 1,000
   arrivals/sec the original implementation exhausted its application pool while
   PostgreSQL remained mostly idle. A managed stack capture found 36 threads in
   `SharedRateLimiter.AttemptAcquireCore`, including synchronous pool waits. The
   intermediate fix made the counter's synchronous probe defer without spending a permit; ASP.NET fell
   back to the async acquire path. The real-database regression failed before
   that change and passed afterward. ADR 54 subsequently removed the entire SQL
   request-counter path; this explains historical experiments, not current code.
2. **Eager read transactions can consume all connections before cross-module
   authorization queries.** The async limiter alone did not recover the workload:
   all 40 pooled connections became idle in transaction while `GrantScopeResolver`
   waited for a connection. A one-connection HTTP test failed on the sites list.
   Fleet read endpoints now use Wolverine's per-handler `Lightweight` transaction
   mode, retaining the declared owner and outbox integration while deferring the
   transaction until persistence. They do not reserve a connection while querying
   another context. Writes retain eager transaction boundaries. The one-connection
   test now passes for list, feed, public list and open-now; detail coverage is included in the final suite. Other transactional
   read/write compositions still need capacity-specific validation; this is not
   a claim that every possible multi-context operation is safe under pool saturation.

This follows the framework's existing mechanisms rather than increasing pool sizes
or weakening RLS: [ASP.NET limiter fallback](https://github.com/dotnet/aspnetcore/blob/v10.0.11/src/Middleware/RateLimiting/src/RateLimitingMiddleware.cs)
and [Wolverine transaction modes](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html).
The application now subscribes to Npgsql and runtime telemetry and identifies
instances by machine and process ID. No diagnostic tenant/user metric labels were
added to the application.

## Results and remaining acceptance

All rows below are fresh local observations with one hot API key, six development
sites and no schedules, one worker, a Node round-robin proxy, and a shared M1 Pro
host (10 logical CPUs, 16 GiB; Docker VM 7.75 GiB). They are individual runs,
not confidence intervals. HTTP failures and dropped arrivals are separate. Later
warmup settings differ explicitly; do not attribute their combined result to one knob.

| Implementation / configuration | Measured duration | Successful req/sec | p95 / p99 ms | Dropped arrivals | Verdict |
|---|---:|---:|---:|---:|---|
| Original sync limiter, eager reads; clean repeat | 30s | <1 | 10,003 / 10,007 | 29,098 | Fails; pool waits/timeouts |
| Async limiter only; eager reads | 30s | <1 | 10,001 / 10,007 | 29,206 | Fails; all 40 connections idle in transaction |
| Async limiter + deferred reads; DB 2 CPUs | 60s | 606 | 498 / 581 | 23,475 | No HTTP errors; capacity/latency fail |
| Same, DB 4 CPUs | 60s | 908 | 340 / 468 | 5,387 | No HTTP errors; capacity/latency fail |
| Same fixes, DB 2 CPUs, shared counter disabled (diagnostic only) | 60s | 969 | 283 / 374 | 1,778 | No HTTP errors; cannot deploy as a fleet configuration |
| Same fixes, DB 2 CPUs, auto-prepare 100, shared counter enabled | 60s | 821 | 380 / 497 | 10,632 | No HTTP errors; capacity/latency fail |
| Same fixes, DB 4 CPUs, auto-prepare 100; 30s target-rate warmup | 120s | 995 | 182 / 294 | 531 | No HTTP errors; still fails strict arrival/latency criteria |
| Same prepared/shared configuration, 2 API + 1 worker | 120s | 968 | 331 / 421 | 3,835 | No HTTP errors; capacity/latency fail |
| Same prepared/shared configuration, 4 API + 1 worker | 120s | 951 | 407 / 569 | 5,874 | No HTTP errors; capacity/latency fail |

The first instrumented original run also failed; a concurrent stuck build made it
a poor capacity comparison, so it was stopped and the original case repeated
without runtime diagnostics. A later stack capture perturbed that repeat briefly;
it confirms the blocking call chain, not an unbiased throughput estimate.

The two-CPU fixed-path run sampled PostgreSQL at roughly 200% CPU (its two-CPU
quota) with no pool-exhaustion logs. Its 36,526 requests executed 73,052 counter
statements; their aggregate server execution duration was 148.3 seconds. Runtime
samples showed 16–22 ThreadPool threads and queue lengths 0–12. These observations,
the separate CPU-quota experiment, and the limiter ablation make the remaining
cost attributable more narrowly than the historical “same laptop” explanation.
Execution durations include waits; they are not database CPU seconds. In the
four-CPU prepared run, samples also show WAL-write and transaction-lock waits:
additional CPU alone does not remove counter contention/commit work.

Native prepared statements improved this repeated SQL workload without a new
service or application cache. This supports evaluating the configuration in staging;
it does not establish a universal gain, a safe arbitrary preparation-cache size,
or a multi-hour memory envelope. The application default remains unchanged.

Current replica, larger-data, fairness and correctness results are recorded in
the [gateway verification ledger](gateway-fairness.md) and summarized in the
[assessment](performance-and-scalability-assessment.md#8-current-position-and-next-decision).
Cross-host generation, production load balancer, realistic tenant distribution,
background writes, hours-long stability and loaded failure recovery remain
required before claiming production capacity.

## External qualification handoff

The proposed disposable hosted target is documented in the
[DigitalOcean deployment plan](digitalocean-test-environment.md).

Use a staging deployment owned by the fork. The Docker reference is a runnable
contract example; its localhost bindings, development providers and shared disk
are not a multi-host deployment recipe. Keep the generator on separate compute;
put APIs on explicitly budgeted compute and hold database and worker resources
constant while changing API replicas. Record gateway/limiter resources too.

1. Pin a reviewed source revision and image digests. Record CPU/memory limits,
   database storage, all connection pools (including messaging and rollout surge),
   gateway policy, TLS/host routing and authentication/audit configuration.
2. Seed a dedicated staging tenant with representative schedules, attributes and
   geographic distribution. Prepare the private fixture described above using
   real credentials. Give only the test tenants and observed test egress addresses
   sufficient operational allowance; save and restore policy around the run.
3. Run gateway correctness controls in an isolated test deployment before load.
   Retain their results separately from throughput. A successful business response
   admitted while the limiter is failing open does not qualify enforcement.
4. Warm each topology at the offered rate for 30 seconds, then measure 5–10 minute
   steps. Start at the demonstrated mixed 250/sec baseline; increase until a gate
   fails. Repeat comparable steps three times in varied order. Test API-key,
   cookie/audited and public traffic separately before the intended combined mix.
5. Compare 1, 2 and 4 fixed-size APIs with added compute. Capture per-instance
   latency/work, gateway decisions and connection counts, runtime/GC/pool waits,
   SQL/waits/WAL, generator pressure, and worker completion/backlog age. A fixed
   1000/sec test that all topologies pass does not measure scale efficiency;
   compare each topology's maximum passing offered rate and resource headroom.
6. Only after short steps pass, run four hours with normal background writes and
   imports. Separately label replica loss, dependency failure and rolling-upgrade
   windows. Provision enough surviving capacity for the required failure case.
   Require final work drain and inspect minute-by-minute resource/backlog trends.

For the measured six-route mix, run the existing script from the generator:

```bash
# PRIVATE fixture: real staging URL(s), credentials, tenant Host and instance IDs.
export FIXTURE=/secure/staging/fixture.local.json
export ROUTES=sites,search,detail,feed,public,near
export RATE=250
CAPACITY_SECONDS=30 EXPECT_BALANCE=false SUMMARY=/secure/results/warmup.json \
  k6 run tools/capacity.k6.js
CAPACITY_SECONDS=600 SUMMARY=/secure/results/summary.json \
  k6 run --out json=/secure/results/samples.json.gz tools/capacity.k6.js
python3 tools/capacity-report.py /secure/results
```

Use a fresh result directory per measurement. For multiple explicit staging
entrypoints set `BASE_URLS` to their comma-separated URLs; otherwise use the
fixture's `base`. The unchanged script gates errors, valid successes, dropped
arrivals, per-route p95/p99 and replica shares. It exercises six equal route
shares, not realistic browser page views or a write workload. If production
does not expose instance headers, set `EXPECT_BALANCE=false` and enforce the
same distribution requirement using load-balancer telemetry; disclose that change.
Collect limiter before/after counters independently when running remotely:
without those counters the generic report does not establish enforcement health.

No independent test hosts were available during this investigation. These steps
are an executable handoff, not evidence that the external or four-hour tests ran.
