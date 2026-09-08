# Bounded-memory investigation

Updated 2026-09-05. Owner: project maintainers. Status: lifecycle fix verified;
complete bounded suite and local full-stack profile pass. Production deployment
sizing remains unaccepted. API handler-loading comparison is now verified.

## Bottom line

The original run exhausted a 4 GiB container, and repeated fixtures retained
disposed applications. The corrected code now releases the hosts and their
limiter timers; twelve-fixture post-GC memory ends near 42 MB instead of 154 MB.
The final complete bounded run passed **302 tests, one expected skip, no failures
or OOM events**. The complete local stack also passed a roughly fifteen-minute
profile with **778 successful measured requests**, scanner reload, and recreation
persistence. A 1 GiB API cap failed; the bounded 2 GiB follow-up passed without
pressure/OOM events. Those service measurements used Dynamic compilation.
The subsequent controlled comparison below passed with **Static pre-generated
handlers at a 512 MiB API cap**, reducing peak process RSS by about **85%**.
This supersedes using the earlier 1–2 GiB API footprint as a replica budget.

Keep hard limits. Do not treat OOM restarts or test sharding as the fix.

The subsequent [concurrency/soak acceptance](concurrency-soak.md) found default
connection-pool starvation without memory pressure. Low RAM use alone does not
establish concurrency readiness; see that report for default versus diagnostic
pool results. Bounded HTTP admission now clears the default-pool 40-client ramp;
its two-hour soak is running, so sustained memory acceptance remains pending.

## API footprint: pre-generated versus Dynamic handlers

Both controlled runs used the same fresh image, one-CPU API quota, synthetic local
adapters/data, separate worker and ClamAV, real PostgreSQL, and the same driver.
Only handler-loading mode and the API ceiling differed. Static ran at 512 MiB;
Dynamic required a 2 GiB ceiling to avoid the already reproduced 1 GiB failure.
Neither run hit its cap, so cap-induced reclaim does not explain the difference.
Phases were 30 seconds idle, two minutes light traffic, and 30 seconds cooldown,
plus startup/upload checks. Each completed 187 requests, clean uploads/downloads,
EICAR quarantine/download refusal, guest SSR data rendering, and zero dead letters.
The synthetic database grew slightly across sequential runs; this is a controlled
local diagnostic comparison, not an independently repeated performance benchmark.

| API measurement | Dynamic / 2 GiB cap | Static / 512 MiB cap |
| --- | ---: | ---: |
| Peak process RSS (MiB) | 1,722.2 | 254.1 |
| Peak proportional set size (MiB) | 1,650.2 | 195.5 |
| Peak container-accounted memory (MiB) | 1,597.7 | 152.2 |
| End container-accounted memory (MiB) | 1,556.8 | 152.2 |
| Peak managed committed memory (MiB) | 82.4 | 38.6 |
| Loaded assemblies at end | 314 | 213 |
| Average load CPU (% of one core) | 8.55% | 7.94% |
| Average cooldown CPU (% of one core) | 2.95% | 2.95% |
| API workload latency median / p95 (ms) | 8.6 / 104.6 | 7.7 / 71.1 |
| Observed max API workload latency (ms) | 449.7 | 130.3 |
| Memory pressure / OOM / kill events | 0 / 0 / 0 | 0 / 0 / 0 |

RSS counts resident process pages, including shared code. PSS apportions shared
pages. Cgroups charge shared pages according to ownership, so container figures
can be lower than RSS and depend on what already shares those pages on the host.
**Use roughly 254 MiB RSS, not 152 MiB alone, when thinking about replica footprint.**
No single measurement is a reservation or a safe maximum at higher concurrency.

The extra Dynamic footprint is predominantly outside the currently committed
managed heap: its last cgroup anonymous memory was 1,434.9 MiB versus 65.0 MiB
Static. File memory was 114.1 versus 82.6 MiB (including shared memory, a subset:
110.1 versus 78.5 MiB). This isolates runtime compilation/loading as the dominant
trigger; it does not attribute every native byte to a specific allocator or
claim that `RSS - GC committed` is an exact native-memory measurement.

CPU comes from cgroup usage deltas and includes the small sampling processes.
The light load was five API reads and one SSR request per five-second cycle,
occasional site writes, and four clean ~1 MB uploads plus EICAR in setup/load.
Latencies cover the selected API paths, including cold calls and site writes;
they exclude the public SSR timings, upload transfer, and poll delays. The load
phase had 0.124 seconds CPU throttling Static versus 0.016 Dynamic: both small,
but these are not maximum-throughput or high-concurrency claims. Steady CPU barely
changed; the main demonstrated gain is memory and cold-route latency.

An initial Auto/1 GiB probe also passed (186 requests, peak RSS 253.4 MiB, container
151.7 MiB). It used a one-line isolated source probe; a brief image build overlapped
that run, so its CPU/latency are not used in the table. The final Static/Dynamic
runs were sequential with no overlapping builds and used the same configurable
image: `sha256:6c48bb5adc90f232d6984a56a1f84d3aba108c9d250d89d010cc548ad5aaf783`.

### What changed and what remains

- `CodeGeneration:Mode` explicitly selects the native enum. Defaults remain
  Dynamic in Development and Auto in Production; invalid text fails startup.
- `compose.profile.yml` now selects Static for pre-generated image roles and
  sets the API cap to 512 MiB. It remains Development/local-only; no guard changed.
- `tools/smoke-image.sh` requires Static and bounds every backend role to 512 MiB.
  It passed in actual Production mode, including migrations, API/worker probes,
  and worker-only durable cleanup. Its S3/KMS endpoints are loopback placeholders
  with dummy credentials; this is boot/wiring acceptance, not live-provider use.
  Strict loading exposed missing S3 endpoint configuration in the previous smoke
  setup, which was corrected without changing product adapters.
- All 36 Production boot-guard tests passed, including the new invalid-mode case.
  Architecture 60/60 and CSharpier checks on both changed C# files also passed.
  The final checked-in Compose configuration also passed the existing Chromium
  loading check (five fresh contexts per UI). This is not the full browser matrix.
  The complete integration suite above predates this small configuration change;
  it was not repeated for this comparison.

Keep the 512 MiB API limit as the next test budget; a larger reservation is not
currently justified by this light workload. Next measure realistic concurrency,
the wider crime-intelligence routes, larger data, restart/rollout overlap and a
longer soak before promising replica density. ClamAV's engine remains outside
each API replica; this work does not implement scale-to-zero or change scan queue
ownership. Stored uploads still must remain quarantined until a successful scan.

No AOT conversion, allocator tuning, GC overrides, or dependency removal was
needed. Static mode avoids runtime compilation; it does not remove the runtime
compiler package from the image. See [Wolverine's code generation guidance](https://wolverinefx.net/guide/codegen)
for native loading modes and pre-generation requirements.

Raw evidence: `/tmp/locintel-memory.kcSvWL/api-static-512m`, `api-dynamic-2g`,
`api-auto-1g`, `codegen-guards`; driver `profile-api.py` and summary
`summarize-api.py` in the parent. Temporary evidence includes logs and synthetic
data and is not a durable backup. Prior Dynamic image retained as
`locintel:memory-profile-dynamic`; `locintel:memory-profile` now names the tested
configurable image.

### Prior complete bounded integration verification

`full-owned-4g`: 411 seconds wall-clock / 6m35s test duration, all 303 cases
accounted for. Architecture **60/60** and platform unit **51/51** also passed.
The build completed without warnings/errors. Product rate budgets, waits,
isolation rules, and telemetry exporters were preserved.

- Sampled cgroup peak: **4,053.9 MiB** of 4,096 MiB.
- New cgroup pressure events: **388** (`max` 2,434 → 2,822); OOM/kill counters
  remained zero. The container was reused, so counter deltas matter.
- Test-host working-set peak: **3,326.0 MiB**.
- Managed committed peak: **414.1 MiB**, last sample **384.7 MiB**.
- Loaded assemblies: **1,171** at the end; runtime compilation still has costs.

Completion under the cap is established for this run, but it is **not generous
headroom** or a VM sizing recommendation. Existing CI shards remain appropriate
for predictable test-runner resource use, now backed by a retention regression.

## Earlier full-stack follow-up (Dynamic compilation)

Fresh backend/public/console images now boot with PostgreSQL and real ClamAV.
The first workload with a **1 GiB API limit failed**: uploads, clean-file download,
EICAR quarantine/download refusal, and data-backed public SSR passed, then the API
was OOM-killed during warmup/browser loading (exit 137, `OOMKilled=true`). Its last
sample was 1,023.7 MiB, with seven cgroup `max` events. This is a genuine failed
resource budget, not a passing run rescued by automatic restart.

Evidence: `stack-api-1g-oom`. The bounded **2 GiB API** follow-up passed; worker
and migrator remain 1 GiB, database/public 512 MiB each, edge 128 MiB, ClamAV 4 GiB.
All have swap disabled. These are ceilings, not measured simultaneous demand.
The local Development configuration still performs runtime compilation; do not
extrapolate its memory directly to a Production/static-codegen artifact.

Packaging also exposed Caddy's inherited `cap_net_bind_service` executable
capability. The image removes that unused capability (it listens on port 8080),
retaining non-root execution, read-only filesystem and `cap_drop: ALL`.
The revised image passes configuration validation under those restrictions.

### Final full-stack measurements

Two minutes idle after warmup, ten minutes of reads/writes and repeated uploads,
two minutes cooldown, then API/worker recreation. The load issued five authenticated
API reads and one public SSR request per five-second cycle, ten site creations,
five clean uploads total (about 1 MB each), and one EICAR test upload. Quarantined
download returned 404; clean downloads matched the submitted bytes. The guest SSR
response contained a site created through the API, not merely an empty-page 200.
The scanner log confirms a database reload and activation of 3,628,050 signatures.

| Service | Hard cap (MiB) | Sampled peak (MiB) | End cooldown (MiB) |
| --- | ---: | ---: | ---: |
| API | 2,048 | 1,407.3 | 1,366.5 |
| Worker | 1,024 | 311.7 | 310.9 |
| PostgreSQL | 512 | 77.2 | 52.4 |
| ClamAV | 4,096 | 1,850.6 | 1,009.9 |
| Public SSR | 512 | 37.0 | 36.4 |
| Console/edge | 128 | 17.7 | 17.4 |
| Migrator (one-shot) | 1,024 | 81.8 | exited 0 |

Five-second sampling observed a simultaneous aggregate peak of **3,634.2 MiB
(3.55 GiB)** and end-cooldown footprint of **2,793.5 MiB (2.73 GiB)**. Samples
are sequential per service, not atomic; brief peaks can be missed. Kernel
`memory.peak` reads before recreation caught **1,408.1 MiB API** and **1,962.0 MiB
ClamAV**. No sampled `max`, `oom`, or `oom_kill` events; all final container states
were non-OOM. Restarts were manual, never automatic recovery hiding failure.

The API plateaued and declined modestly after warmup; the worker warmed as it
handled work. Neither returned to its cold-start baseline. This demonstrates
bounded behavior for this workload, not indefinite leak freedom or maximum
throughput. Container memory includes more than the managed heap.

The existing Chromium loading check passed five fresh contexts for each UI,
without page errors before the expected heading. Console median heading-ready
time was 234 ms (cold first sample 3,376 ms); public SSR median 537 ms. These are
local, unthrottled heading checks, not full interaction/performance acceptance
or a replacement for the currently incomplete browser matrix.

After recreating API/worker, the original session stayed authenticated and the
previous clean file downloaded byte-for-byte. Dead letters were zero. Both roles
can consume messages in this topology; this is not proof of worker-only delivery
or a deliberately accumulated backlog draining. Database and object backup/restore
were not exercised. The profile reused local synthetic volumes from setup attempts.

Evidence: `/tmp/locintel-memory.kcSvWL/stack` (`memory.csv`, `result.json`, service
logs and before/after container inspections); disposable driver `profile-stack.py`
and summary `summarize-stack.py` live in its parent. Images tested:

- Backend: `sha256:efc0b324546b99ab49903104fcf7d8709805ad473c40fe9c554d6d841cf91e2a`
- Public: `sha256:1789e4d759cb8f4b68617e5d75ccd2cff4ee503264d2351ed96f47144c239d2e`
- Console/edge: `sha256:48adcbef1c1338a6daa2161cfa10813688aa37e3ab4c3f0ecccd3a9ccb082062`

### Earlier sizing conclusion (superseded for pre-generated API replicas)

**2 GiB total is ruled out for this configuration; 4 GiB is tight**, given the
observed reload peak before OS, backup, and deployment overlap. An 8 GiB envelope
is the safer next sizing experiment, not authorization to buy a server. The final
long-lived caps sum to 8.125 GiB: service limits alone do not prevent aggregate
host exhaustion. Tune/reserve aggregate headroom using a production-shaped soak,
scanner signature updates, and restore/deployment overlap before choosing a host.
These statements describe the earlier Dynamic profile, not the new Static API
budget. Re-measure the complete revised topology before selecting a host; do not
subtract isolated peaks and call the result a verified aggregate peak.
Do not disable concurrent scanner reload or signature validation to force a fit.
ClamAV's [container guidance](https://docs.clamav.net/manual/Installing/Docker.html)
recommends 4 GiB and explains its reload/validation transients.

## Lifecycle fix follow-up

### Retention investigation chronology

The repository now has a failing-before/passing-after `HostLifetimeTests` check:
three real migrated/seeded hosts serve an authenticated request, are disposed,
and their service providers must become unreachable after full GC. The original
code retained all three. No rate budgets, timeouts, RLS policies, or telemetry
exporters were disabled to obtain the passing result.

The controlled probes separated three paths:

1. Disposing only the configured global limiter still retained all three hosts.
   ASP.NET Core also creates an internal endpoint limiter with no disposal path;
   this matches [upstream issue #66434](https://github.com/dotnet/aspnetcore/issues/66434).
   LocIntel uses global quotas only. Its small pipeline adapter now calls the
   same native chained partitioned limiter, owned/disposed by DI, without
   constructing that unused endpoint limiter. Rate-limit metrics, rejection
   logging, HTTP 429, and Retry-After are retained. Endpoint policy/queue support
   is not introduced; revisit the adapter when the upstream fix is available.
2. EF's cached tenant-filter expression held the first live `IdentityDbContext`,
   which held its logger/provider and the disposed host. The shared filter now
   uses a typed context reference that EF replaces at query time, without
   capturing a live context. `TenantFilterBindingTests` checks distinct A/B/empty
   query parameters on the same cached model, independently of PostgreSQL RLS.
3. OpenTelemetry's one-time SDK initialization could capture the startup host
   context in its process-wide self-diagnostic worker. Only that synchronous
   initializer suppresses execution-context flow; normal startup and request
   flow remain unchanged, and all configured tracing/metrics/logging stay enabled.

The combined lifecycle, tenant isolation, quota, and HTTP-hardening run passed
**22/22**. After preserving the rate-limiter instruments and adding an emission
assertion, lifecycle plus HTTP hardening passed **4/4**.

The complete 4 GiB follow-up then finished: **300 passed, two Azure-emulator
setup failures, one expected skip (303 total)**; 441 seconds wall-clock, 7m4s
reported test duration. `oom=0` and `oom_kill=0`. It is not an all-green suite.
Sampled cgroup peak was **4,095.9 MiB**, with **2,434 `max` events**: the cap was
actively constraining memory and this is not comfortable headroom. Test-host
working-set peak was **3,086.8 MiB**; managed committed memory briefly peaked at
**2,052.3 MiB** and its last sample was **376.7 MiB**. Assemblies reached 1,173.
The fix removes retained host graphs, not runtime compilation's transient costs.
Evidence: `full-fixed-4g` under the directory below. This run precedes the small
final adjustment to resolve the same DI-owned limiter at startup rather than
on the first request; that adjustment avoids capturing request context.

The subsequent twelve-fixture probe exposed an additional ownership detail:
disposing the chained native limiter does **not** dispose its child limiters.
With startup initialization, managed memory again rose from 37,321,768 bytes to
149,884,576 bytes and the collectible-host check failed (`child-ownership-red`).
The first-request variant had hidden this host-retention path, not correctly
owned its timers. DI now separately owns the org limiter, principal limiter,
and chain. The collectible-host regression passes with startup initialization
(`ownership-and-azure`: one lifecycle pass, two independently reproduced Azure
setup failures). The corrected twelve-fixture run passed in 58 seconds:

| Disposed lifetimes | Original post-GC bytes | Correctly owned post-GC bytes |
| --- | --- | --- |
| 1 | 41,258,744 | 26,939,424 |
| 4 | 73,800,872 | 39,944,176 |
| 8 | 113,366,720 | 40,978,400 |
| 12 | 154,281,648 | 41,864,192 |

Growth across lifetimes 4–12 fell from 80,480,776 to 1,920,016 bytes (about 98%).
Some runtime/model/code-generation warm-up remains; both runs loaded two more
assemblies per repeated fixture. This is not a zero-allocation or unlimited-soak
claim. The regression also now checks collection of both child limiters and
their chain, independent of whether context capture roots a host.
Evidence: `fixtures-owned` and `fixed-fixtures.csv`; the unsuccessful intermediate
curve was preserved as `partial-ownership-fixtures.csv` / `fixtures-fixed`.

The Azure setup failure was separately isolated: with `host.docker.internal`,
Azurite received `/locintel-test?restype=container` instead of an account-prefixed
path and treated `locintel-test` as the account. Both existing Azure tests passed
when the runner used the resolved Docker gateway IP (`azure-ip`). No production
adapter change, loose emulator mode, or version-check bypass was needed. Resolve
the gateway for each new environment; do not hard-code this machine's IP into
the repository. The temporary emulator debug instrumentation was removed.

Evidence under `/tmp/locintel-memory.kcSvWL`: `lifetime-baseline`,
`lifetime-dispose-global`, `lifetime-startup-flow`, `lifetime-global-only`,
`lifetime-filter-placeholder`, `lifetime-no-suppression`,
`lifetime-targeted-telemetry`, `retention-and-guards`, and `final-lifecycle`.
The remaining-first-host dump and concise EF root are `remaining-host.dmp` and
`remaining-host-root.txt`. Raw dumps stay out of version control.

## Original measured results (before the fix)

Memory below is sampled cgroup `memory.current`, including charged file cache
and diagnostic processes where applicable, not just the managed heap. MiB is
2^20 bytes. Two-second test sampling and roughly five-second service sampling
can miss short peaks.

| Experiment/process | Hard cap, no extra swap | Sampled peak | End/cooldown | Result |
| --- | --- | --- | --- | --- |
| Full integration runner, 2 CPUs | 4,096 MiB | 4,095.8 MiB | Test host killed | cgroup recorded one OOM kill |
| API, 1 CPU | 1,024 MiB | 802.7 MiB | 653.2 MiB | Workload completed |
| Worker, 1 CPU | 1,024 MiB | 392.9 MiB | 392.9 MiB | Ready; no OOM |
| PostgreSQL, 1 CPU | 512 MiB | 133.1 MiB | 133.1 MiB | No OOM |

The full integration attempt lasted approximately 517 seconds wall-clock:
285 passed, two Azure-emulator setup failures (HTTP 400), one opt-in skip,
then an aborted test host. It did **not** finish all 301 tests. The cgroup
`oom_kill` counter rose from zero to one. A missing Kerberos-library warning
also appeared in output; it is not evidence that this warning caused the OOM.

Before termination, the test host had loaded 1,104 assemblies. Its last-collected
managed heap was approximately 1.1 GiB, substantially less than its nearly
4 GiB total footprint. Large transient allocations, retained managed objects,
runtime/code-generation costs, and other memory must not be conflated.

## Reproduced fixture retention

A disposable test in an isolated source copy repeated the real `ApiFixture`:
create PostgreSQL, migrate/seed, boot the API, sign in, read roles, dispose the
client and fixture, then force full GC/finalization/full GC. No mock persistence
or production-code changes were used. This reproduces growth **serially**, so
parallelism is not necessary for this retention path.

| Completed/disposed fixture lifetimes | Managed memory after forced GC | Loaded assemblies |
| --- | --- | --- |
| 1 | 41,258,744 bytes | 338 |
| 4 | 73,800,872 bytes | 344 |
| 8 | 113,366,720 bytes | 352 |
| 12 | 154,281,648 bytes | 360 |

Growth was roughly 10 MB per additional fixture after warm-up. Heap snapshots
after lifetimes four and twelve corroborate it:

- `WebApplication`: **4 → 12** retained instances.
- `WolverineRuntime`: **4 → 12** retained instances.
- Routing `DfaState[]`: **4 → 12** arrays.
- OpenTelemetry `MetricPoint[]` in the 144,168-byte bucket: **104 → 312** arrays
  (approximately 15 MB → 45 MB in that bucket alone).
- Heap snapshot totals: **74,840,219 → 155,056,796 bytes**.

A separate four-fixture run reproduced the trend and provided a heap dump.
Non-stack GC-root analysis showed paths through timer queues, partitioned
rate-limiter timer tasks, captured `ExecutionContext`, the test host resolver's
`HostingListener`, `DeferredHostBuilder`/`WebApplicationFactory`, and the disposed
application graph. Another observed path included OpenTelemetry self-diagnostic
background work. These are verified retention paths, not proof that every byte
in the original macOS 38.6 GB incident has the same cause.

The framework's rate-limiting middleware creates its own endpoint limiter as
well as consuming the configured global limiter. A proposed fix must account
for both lifetimes; simply clearing local fixture variables or disposing only
one configured object is not yet a demonstrated solution.
See the [versioned ASP.NET Core source](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.10/src/Middleware/RateLimiting/src/RateLimitingMiddleware.cs).

## Separate long-lived service experiment

Used the existing `locintel:staging-local` artifact:
`sha256:d1ce8ed29a6f419612eae5d6ef4f67e9e8af7a0a592e838c4686e315dfde77f5`.
This image predates the final SMS refusal-message correction; it is not a fresh
image of every current working-tree edit.

The one-shot migrator completed with a 1 GiB cap. API and worker ran as the
image's non-root user with independent memory caps, against a fresh disposable
database. Environment was explicitly **Development**, with local authentication,
local storage/secrets/notification adapters, synthetic data, and SMS off. No
Production boot guard was changed. This also means runtime code generation and
dev seeding differ from a Production boot with pre-generated handlers.

Phases: approximately one minute idle after startup, three minutes of traffic,
one minute cooldown. The workload made **216 successful measured requests**:
180 reads across `/me`, sites, roles, incident statistics, and alert summaries;
36 site creations. Setup/sign-in/hierarchy creation were additional requests.
Readiness was verified for API and worker; the final dead-letter count was zero.

API memory warmed up, then declined during continued traffic and cooldown.
Its assembly count settled at 305; the worker was at 301. End-of-run managed
committed memory was approximately 73 MiB for API and 46 MiB for worker.
Those heap numbers are **not** substitutes for the larger container footprints.
Worker memory rose modestly across the short run; a longer soak is still needed.

The measured services finished around **1.15 GiB combined**. Their individual
peak totals sum to about **1.30 GiB** (not necessarily simultaneous). This leaves
too much unmeasured to approve a 2 GiB deployment: scanner, public SSR, console
server/proxy, operating system, backups, migrations, and deployment overlap are
not included. Do not choose or purchase a VM from these results alone.

## What happens next, in order

1. **Completed:** fix and regression-test disposed-host retention at the correct lifecycle
   boundary. Require old hosts to become collectible and the post-GC baseline
   to stop increasing proportionally with fixture count. Preserve rate-limit
   semantics, tenant isolation, and observability; do not disable them for a pass.
2. **Completed:** repeat the complete bounded suite after that fix. Resolve the containerized
   Azure-emulator setup issue separately. Keep the previously green two-shard
   local run as evidence, not as proof that retention is solved.
3. Address shared-identity browser rate-budget interference, then finish the
   Chromium/Firefox/WebKit gate. This investigation did not change those tests.
4. **Local baseline completed:** fresh images, full stack, uploads/scanning,
   SSR, reload, and recreation persistence. Next prove deliberate worker backlog
   drain and backup/restore, then run a longer production-shaped soak including
   deployment overlap. Establish aggregate host headroom before provider selection.

The follow-up changed lifecycle code and added regression tests; no merge commit,
hosted deployment, provider selection, or spending was performed.
`compose.profile.yml` now captures explicit limits for a local synthetic full-stack
profile; it is not a hosted deployment configuration.

## Reproduction and evidence

Private local evidence: `/tmp/locintel-memory.kcSvWL` (temporary, not a durable backup).
It contains `measure.py`, `roles.py`, isolated source and fixture experiments,
runtime/cgroup CSVs, test reports, two managed-heap snapshots, a heap dump, and
`roots/gcroot.txt`. The diagnostic toolchain was SDK 10.0.302/runtime 10.0.10 on
Linux musl ARM64; the original failure was native macOS. OS/runtime/CPU differences
prevent direct timing comparisons. The serial fixture experiment removes
concurrency as a prerequisite but does not establish identical cross-OS costs.

The diagnostic container had `memory.max=4294967296`, `memory.swap.max=0`, and
a two-CPU quota. Its Testcontainers dependencies ran outside that cgroup under
Docker Desktop's roughly 7.75 GiB aggregate VM budget; the 4 GiB number is **not**
the total integration-stack budget. Diagnostic Docker-socket access was explicitly
approved for this temporary runner. Raw snapshots/logs stay outside the repository
and can contain in-memory synthetic credentials; temporary storage is not durable
backup. Tooling follows [Microsoft's container diagnostic guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostics-in-containers).

Cleanup verified: diagnostic runner, temporary image builder, all profile
containers and networks removed. Docker-socket access ended with runner removal.
Images/evidence remain. The final Compose database, app-data/keyring, and scanner
signature volumes remain intentionally for local reproduction; no user data was
deleted. The earlier separate service-profile database was disposable and removed.

Related: [staging status](container-staging.md), [project overview](../README.md),
[production configuration](production.md).
