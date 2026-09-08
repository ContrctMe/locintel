# Concurrency and soak acceptance

Updated 2026-09-05. Owner: project maintainers. Status: bounded HTTP admission
implemented; baseline and new-image two-hour tests completed. Deployment remains unapproved.

## Bounded admission remediation

Subsequent [transaction-lifetime work](transaction-lifetimes.md) changes reviewed
read handlers in source. This soak still measures the admission-only image below;
do not attribute its results to those later changes. Separate-database regression
tests and local builds overlapped portions of the soak (host-contention caveat).

The unchanged implementation failed
`dotnet test tests/LocIntel.IntegrationTests --filter FullyQualifiedName~Concurrent_gated_reads_complete`
with eight authenticated incident-list requests and a four-connection pool (500s
after the test's three-second acquisition timeout). The same test passed after
adding the native .NET `ConcurrencyLimiter` before database-using HTTP middleware.
No endpoint transaction or gate was removed, and no cross-module transaction was
introduced (ADR 17).

`HttpPolicyHosting` admits `Maximum Pool Size / 2` active requests and at most
`Maximum Pool Size * 2` queued requests, FIFO. At the default pool20 this is ten
active and forty queued. This deliberately trades peak parallel execution for
headroom: eager module transactions can retain connections while authorization
queries another context. It is process-local HTTP admission, not a reservation
inside Npgsql or a universal guarantee against arbitrary background workloads.
Background/messaging pools and all replicas still require an aggregate database
budget. Pool sizes below two are refused at boot because even one transactional
request needs a second connection for its gate.

Queued work observes request cancellation. Queue overflow returns 503 with
`Retry-After: 1`; existing per-user/org rate quotas and their 429 behavior are
unchanged. `/livez` and `/healthz` bypass admission, not database readiness checks.
The limiter is DI-owned and disposed with the host. No new dependency or setting.

Verification completed: seven pool/admission tests (including mixed read/write,
cancellation, overflow, health and budget validation); 60 architecture tests;
72 focused gate/isolation, concurrency, HTTP-policy, host-lifetime, worker-role
and Production-guard checks; release build; Static Production-mode migrate/API/
worker image smoke with durable cleanup. This is not a new full-suite result.

Candidate `locintel:admission-profile`:
`1a36ca5159b4fa5066de157ad0cbb0e07a7405cb511b08f17b7f82f7a8645546`.
Runtime base digest matches the previous image; build SDK changed from 10.0.302
to the local 10.0.102. The new run retains default pool20, the existing caps and
Static loading, ramps 1/5/10/20/40 clients, then runs the same 20-client workload
for two hours plus three minutes cooldown. The first 30 minutes of soak are the
acceptance checkpoint, with continued observation resolving longer-term drift.
The ramp passed all stages: 12/60/120/240/480 successful requests respectively.
At 40 clients, p50 was 96.1 ms, p95 236.7 ms, p99 277.2 ms and maximum 283.2 ms;
no failed requests or memory-limit events. This reproduces and clears the
previous default-pool failure. The two-hour result is recorded below.

### Completed admission-only two-hour baseline

The driver completed 46,835 counted successful requests, including 44,760 soak
workload requests (all HTTP 200), with zero durable dead letters. Clean/EICAR
checks, scanner reload, SSR, and final session/file checks passed. Containers and
network were removed; evidence and synthetic named volumes remain. There was no
container recreation between soak and the final persistence assertions.

Soak latency was p50 40.2 ms, p95 403.4 ms, p99 1,709.7 ms, maximum 5,893.9 ms.
Independent health probes recorded 3,780 HTTP 200 responses and **one timeout**
at elapsed 5,954.59 seconds. The subsequent direct API liveness probe also timed
out; later direct readiness and edge liveness returned 200. The database snapshot
showed idle clients, not an executing lock wait. These sequential diagnostics do
not establish the transient's cause. Overlapping regression/build work confounds
tail latency; this is not a clean causal performance baseline or all-green health result.

| Service | Sampled peak MiB | End cooldown MiB |
| --- | ---: | ---: |
| API | 306.0 | 305.1 |
| Worker | 146.4 | 145.9 |
| PostgreSQL | 141.7 | 87.1 |
| ClamAV | 2,154.6 | 1,220.3 |
| Public SSR | 113.4 | 110.0 |
| Console/edge | 61.9 | 59.7 |

No service recorded OOM, OOM-kill or memory-limit events. Combined sampled memory
peaked at 2,783.4 MiB and ended at 1,928.1 MiB, excluding host/diagnostic overhead.
API CPU averaged 7.75% of one core; cumulative soak throttling was 103.5 seconds.
The API retains useful headroom inside 512 MiB, but total cgroup memory did not
plateau: five-minute medians rose from 203.9 to 304.7 MiB. Anonymous memory settled
around 145–147 MiB after the first hour while file-backed accounting continued
from 92.6 to 149.5 MiB (shared memory is included, not an additional category).
Late RSS medians were stable near 350–351 MiB. Managed committed memory peaked
at 59.6 MiB and ended at 55.1 MiB. This points toward later file-backed growth,
not demonstrated continuous managed-heap growth; it does not prove leak freedom
or that all file-backed memory is reclaimable. Keep the cap and investigate
longer-term cache behavior before promising a fixed per-replica footprint.

The transaction-lifetime image is being tested separately, retaining the same
budgets and synthetic volumes. Accumulated fixtures and added diagnostics are
additional comparison caveats. No causal hold-time improvement can be inferred
from the old baseline, which did not collect EF lifetimes or native Npgsql metrics.

The new-image pilot passed (511 counted successes, 400 soak requests, 58/58 health
probes, zero dead letters/OOM). Its containers were cleaned up before starting
`transaction-pool20-2h --comparison --two-hours`; evidence is under
`/tmp/locintel-memory.kcSvWL/transaction-pool20-2h`, log `transaction-run.log` in
the parent. The full result is recorded below. Both runs' containers and temporary
diagnostic runners were removed; evidence, images and synthetic volumes remain.

### Completed transaction-lifetime two-hour comparison

Image `8aa6d4d8ab65741868e0027df009e76ba23289441857abdbb756c1db6345b63b`
completed **49,698 counted successful requests**, including **47,560 soak requests**
and 912 ramp requests. All **3,819 health probes returned 200**. Final dead letters
were zero; clean/EICAR checks, scanner reload, SSR, and session/file checks passed.
The driver exited zero after cleanup. Final container snapshots showed no OOM
and successful migration; no memory-limit events occurred throughout the run.
This is local synthetic acceptance, not production-provider or restore acceptance.

At 40 clients, p95 was 154.7 ms. Soak p50/p95/p99/max were
54.1 / 186.0 / 277.5 / 806.0 ms. API CPU averaged 6.89% of one core;
cumulative cgroup throttling was 132.5 seconds. Better tail latency than the old
run is descriptive, not causal: that run overlapped heavy validation, this one
adds diagnostics, and both reuse accumulating fixtures. Median latency did not
improve (old 40.2 ms). Do not treat either run as a saturation benchmark.

| Service | Sampled peak MiB | End cooldown MiB |
| --- | ---: | ---: |
| API | 317.0 | 315.3 |
| Worker | 139.5 | 139.0 |
| PostgreSQL | 101.6 | 64.7 |
| ClamAV | 1,965.3 | 1,027.2 |
| Public SSR | 39.1 | 36.6 |
| Console/edge | 22.1 | 19.7 |

Combined sampled memory peaked at 2,460.1 MiB (2.40 GiB) around reload and ended
at 1,602.6 MiB (1.57 GiB), excluding host/Docker/diagnostic overhead. AV separation
still matters: its steady roughly 1 GiB is not duplicated inside each API replica.
The 512 MiB API cap has tested headroom, but **total memory has not plateaued**.
Five-minute API cgroup medians rose 202.5→313.9 MiB; file-backed memory rose
92.3→154.1 MiB, including shmem 89.2→93.1 MiB. Anonymous memory rose
105.8→153.1 MiB, with much slower late growth around 152–153 MiB. Late cgroup
growth was roughly 30 MiB/hour, mostly file-backed; do not extrapolate indefinitely
or assume it is all reclaimable cache. RSS peak/end were 360.1/359.5 MiB and PSS
300.4/299.8 MiB. Managed committed memory peaked at 60.0 MiB and ended at 58.1 MiB;
assemblies stabilized at 215 and thread-pool queue ended at zero (sampled max15).
There is no demonstrated managed-heap runaway, but no demonstrated bounded
long-term total footprint either. A smaller API cap is not justified yet.

Native Npgsql metrics provided 3,849 samples: regional pool max20, used0–10,
idle0–12, ending used0/idle2. Sampling misses short peaks. Pending-request and
timeout series were absent, not measured zero. Across 2,535 PostgreSQL snapshots,
connections were overwhelmingly idle; one grouped row was idle in transaction.
The oldest observed transaction age was 3.688 ms. These are sparse age observations,
not completed lifetime percentiles or proof that no long transaction occurred.
The pilot trace remains available; its ID/timing payloads were verified, but
correlation and an equally instrumented old-image window remain outstanding.

#### Next priorities

1. Dominant file-backed growth is attributed to local-upload page cache; bounded
   automatic reclaim and local S3-path controls now pass (see below). Keep the
   512 MiB API cap. Warm mixed-workload production-storage sizing and aggregate
   host budgeting remain before selecting smaller replica budgets. Do not add
   cache eviction or restarts to application code merely to flatten the graph.
2. Correlate the existing pilot lifecycle trace with boundary handling; obtain
   an equally instrumented short baseline only if a causal lifetime comparison
   is needed. Keep admission and current pool budgets until mixed-write/background
   and replica/database budgeting tests justify changes.
3. Preserve readiness/teardown recurrence diagnostics; the earlier regression
   failures remain unexplained despite three clean shard replays. Then complete
   replica budgeting, production-provider, backup/restore and hosted acceptance
   with an explicitly agreed deployment target. No additional long soak was launched.

### File-cache attribution investigation (2026-09-05)

**Finding:** the dominant continuing growth in the completed soak is filesystem
page cache for synthetic uploads handled by the local storage adapter. It is not
evidence of continuously retained managed upload buffers. This narrows the earlier
unresolved-growth concern; it does not establish that all memory is bounded.

Artifact replay, starting at the first soak sample, found exactly 59 increases in
`inactive_file`, each **1,064,960 bytes**: the 4-KiB-page-rounded size of the driver's
**1,064,000-byte** clean upload. The steps total 62,832,640 bytes (59.92 MiB), with
**zero net change between these steps**. Total file accounting rose 64.80 MiB;
the remaining 4.88 MiB was shared-memory growth. During the latter half alone,
non-shmem file growth was 30.47 MiB, anonymous growth 3.95 MiB and shmem 0.40 MiB.
The recorded page-scan/reclaim counters stayed zero: the old run did not force
reclamation, so retained cache by itself cannot demonstrate failed reclamation.

The deterministic artifact signal is runnable in under a second:

```sh
python3 /tmp/locintel-memory.kcSvWL/replay-file-growth.py transaction-pool20-2h --assert-plateau
```

It deliberately fails with `NO PLATEAU: late non-shmem file growth exceeds 8 MiB`.
That historical artifact should remain red, not be rewritten to manufacture a
pass. Eight MiB is a diagnostic discriminator, not a product memory SLO.

#### Live differential control

The temporary `file-cache-control.py` uses the same built image and unchanged
512-MiB API cap, with no EventPipe/counter collector attached. It exercises actual
ticket creation, local HTTP PUT, completion, real ClamAV scanning and downloads.
After one warm-up file, it creates twelve new files, rereads them, idles, applies
`fsync` and `POSIX_FADV_DONTNEED` to **only those twelve exact new file paths**,
then downloads again and checks every byte. No global cache drop, forced GC,
file deletion, restart during measurement, product change or limit increase.

First control, 12,768,000 payload bytes:

| Measurement | MiB |
| --- | ---: |
| Inactive file-cache increase after new uploads | 12.195 |
| Additional increase after repeat downloads | 0 |
| Additional increase during ten-second idle | 0 |
| Cache released by targeted advice | 12.137 |
| Cache restored by byte-verified downloads | 12.188 |

The whole control passed its assertions and exited zero. The cache release is
direct evidence that these pages were discardable without losing object data;
the reload provides the reverse intervention. Small deviations reflect page
rounding and concurrent activity. This is a short storage-focused control, not
a replacement for the mixed workload or a warm-RAM benchmark. Collector absence
shows a collector is not necessary for this growth; it does not quantify all
instrumentation overhead. Initial runtime/shmem warm-up still occurs.

A second fresh-stack control passed and exited zero as well: the same 12.195-MiB
increase, zero additional inactive-file growth on reread/idle, then exactly
12.1875 MiB released and exactly 12.1875 MiB reloaded. Both controls' profile
containers/networks were removed; all synthetic objects, volumes and evidence
remain. No diagnostics runner or recurring experiment was started.

Evidence and runnable diagnostic helpers (temporary, private):
`/tmp/locintel-memory.kcSvWL/file-cache-control-1` and `file-cache-control-2`, sibling `.log` files,
`file-cache-control.py`, `cache-advice.cs`, and `replay-file-growth.py`.
The helper is a portable managed DLL published under `api-ipc/cache-advice`; it
is not shipped. The first publish attempted native AOT and failed on a local
linker dependency; republishing with `PublishAot=false` succeeded. This was a
diagnostic build issue, not an application failure.

Run a new unique evidence directory after publishing the helper:

```sh
dotnet publish /tmp/locintel-memory.kcSvWL/cache-advice.cs --self-contained false -p:UseAppHost=false -p:PublishAot=false -o /tmp/locintel-memory.kcSvWL/api-ipc/cache-advice
python3 /tmp/locintel-memory.kcSvWL/file-cache-control.py UNIQUE_EVIDENCE_NAME
```

The harness refuses an existing profile stack and removes only its stack at exit.
Its assertions check payload-sized cache growth/release/reload and unchanged bytes.
It intentionally does not assert that total memory is flat during runtime warm-up.

#### Implications and limits

`LocalStoreEndpoints` handles upload/download bytes inside the API, and
`LocalObjectStore.WriteAsync` writes them to the persistent local volume. The
existing S3 adapter instead returns presigned client PUT/GET URLs; the local
adapter is prohibited in Production. The extra local filesystem-cache cost is
therefore not a measured requirement of the production API data path. Worker
scanning and object-store resource costs still exist, and self-hosted storage
would still consume host resources. No production-provider run is claimed here.

Do not "fix" this by flushing cache on every upload: that would discard useful
cache and add I/O. Keep the existing limits and report total cgroup memory with
its anon/shmem/file components. A targeted successful advice call is **not** proof
of automatic reclamation or latency under memory pressure. Nor does subtracting
inactive cache yield a guaranteed safe lower cap. Residual slow anonymous/shmem
growth and representative production-storage behavior remain sizing caveats.

Kernel interpretation: [cgroup v2 memory accounting](https://www.kernel.org/doc/html/latest/admin-guide/cgroup-v2.html)
includes cached filesystem data in `file` and shared-memory data in `shmem`;
the LRU categories are not universally interchangeable with type categories.
[Linux cache advice](https://man7.org/linux/man-pages/man2/posix_fadvise.2.html)
attempts to discard cached pages; dirty pages require writeback first.
The attribution above rests on measured matching deltas and intervention, not
solely on a counter label.

### Automatic reclaim and direct-to-S3 controls (2026-09-05)

Both additional controls passed and exited zero using the same transaction-lifetime
image, existing API 512 MiB memory/swap cap and one CPU. No EventPipe collector,
application changes, cache eviction calls, limit increases or mid-test restarts.
These extend the earlier targeted-advice result; they are not another long soak.

#### Automatic reclaim under actual cgroup pressure

The driver uploaded twelve 8,512,000-byte files after a warm-up upload, using real
local storage, completion, ClamAV scans and byte-verified downloads. API inactive
file cache reached 105.64 MiB. A temporary helper in the **same API cgroup** touched
native allocations in 8-MiB increments, stopping at 344 MiB allocated and holding
them for fifteen seconds. It has a hard maximum of 352 MiB and stops earlier when
current memory minus inactive-file memory exceeds 464 MiB. This is artificial
allocation pressure, not a claim that production requests allocate this amount.

- Sampled total memory reached 511.42 MiB under the unchanged 512 MiB cap.
- Inactive file cache fell to 29.86 MiB: **75.78 MiB automatically reclaimed**.
  `pgsteal` increased by 19,392 pages. No `fadvise`, `drop_caches` or forced GC.
- `memory.events max` rose to 306, showing the limit/reclaim path was exercised;
  OOM and OOM-kill remained zero. Limit encounters are expected in this pressure
  experiment and must not be confused with the earlier zero-limit-event soak.
- All nine health requests passed, 3.47–9.94 ms. All twelve files passed byte
  checks after the helper exited. Total memory then sampled 158.62 MiB before
  reloading those files. The container did not restart to obtain that reduction.

This proves successful automatic reclamation in this bounded local scenario,
including responsiveness of readiness. It does not prove mixed endpoint latency
under sustained pressure, arbitrary allocation safety, or leak freedom.

#### S3-compatible direct client uploads/downloads

The subsequent fresh stack selected the existing S3 adapter for API and worker,
backed by local MinIO with its own **512 MiB / one CPU** budget. It retained local
identity/secrets and real ClamAV: this is production-style **storage**, not a full
Production-environment/provider acceptance run. No external bucket, spend, or
cloud credentials were used. MinIO and its bucket-init helper were bounded too.

The client used the API-issued signed URLs and ticket headers to PUT and GET
directly to loopback MinIO port19000, preserving the signed Host header for
`host.docker.internal:19000`. Docker services used that same storage origin.
This host-resolution accommodation does not rewrite signatures or proxy file
bytes through the API. Completion and worker scans passed before downloads.

Twelve new files totaled **102,144,000 bytes (97.41 MiB)**, excluding warm-up.
Every download and repeat download matched its source bytes:

| Metric | After warm-up | After uploads | After repeat downloads |
| --- | ---: | ---: | ---: |
| API cgroup memory MiB | 107.05 | 125.66 | 126.60 |
| API inactive file MiB | 0.004 | 0.004 | 0.004 |
| MinIO cgroup memory MiB | 351.61 | 482.80 | 496.76 |
| MinIO file-backed memory MiB | 120.91 | 218.58 | 218.58 |

API inactive file-cache growth was **zero**. MinIO file-backed growth was about
97.66 MiB, consistent with storing those bytes plus filesystem overhead. Its
anonymous memory also grew 227.34→271.60 MiB. Sampled API and MinIO OOM counters
were zero; MinIO's sampled limit-event count stayed zero. MinIO ended close to its
cap, so this is **not** approval to size a long-running object store at 512 MiB.
Docker's displayed memory can subtract inactive cache; the table uses raw cgroup
readings and must not be mixed with the earlier one-off `docker stats` display.

**Sizing decision:** retain the API's 512 MiB budget. The 126.6 MiB short-test
reading is not the mixed-workload warm footprint; do not reduce the cap from it.
The direct storage path removes this local-file cache cost from API replicas,
but self-hosting MinIO adds a separate material footprint alongside AV, worker
and database. Hosted object storage moves that hosting cost off the API host,
not out of the system. Deployment choice and representative warm/mixed sizing
remain open; no hosted sizing/pricing claim or new multi-hour run is made.

Evidence: `/tmp/locintel-memory.kcSvWL/automatic-reclaim-1` and
`/tmp/locintel-memory.kcSvWL/s3-storage-control-1`, with sibling `.log` files.
`file-cache-control.py` now supports `--pressure` and `--s3`; `cache-pressure.cs`
is the temporary bounded allocator and `s3-control-override.yml` the private
Compose override. The pressure helper was published with `PublishAot=false`.
Assertions require meaningful reclaim, kernel reclaim counters, successful
health/byte checks, and absence of upload-sized API file growth for S3.
Both stacks' containers/networks were removed after completion; synthetic files,
the new `s3-control-data` volume and all evidence remain. No product source changed.

Live evidence: `/tmp/locintel-memory.kcSvWL/admission-pool20-2h` and
`/tmp/locintel-memory.kcSvWL/admission-run.log`; driver `profile-admission.py`,
private image/diagnostics override `admission-override.yml` in the parent.
The original profile image and old failure evidence are preserved. No commit,
push or deployment was performed.
The in-task follow-up finalized the new-image test and is paused after completion.
Automation id: `locintel-two-hour-soak-follow-up`. It must verify cleanup of
the profile and temporary diagnostics runner without deleting evidence/volumes.

### Mixed-workload S3 follow-up

The user approved the next warm/mixed storage-path measurement. The existing
`profile-admission.py` now accepts `--comparison --s3`, retaining its HTTP/session
workload and limits while routing signed upload/download URLs directly to local
MinIO. It validates the storage origin, preserves the signed Host, sends no API
session cookies to storage, and handles the completed bucket-init job like migrate.
Per-service memory includes MinIO; API, worker and MinIO have detailed cgroup CPU,
memory and process snapshots. API runtime/native pool counters are enabled again.
The modified harness passed `s3-mixed-pilot --comparison --s3 --pilot`: 512 counted
successful requests, all 61 health probes returning 200, zero dead letters and
normal exit after cleanup. Clean/EICAR checks and the detailed telemetry passed.

After pilot acceptance, `s3-mixed-30m --comparison --s3` started: one-minute
ramps through 1/5/10/20/40 clients, 20 clients for 30 minutes, then three minutes
cooldown. It includes writes, clean scans/downloads, EICAR quarantine during setup,
SSR, scanner reload and final session/file/dead-letter assertions. It remains a
synthetic local-identity/secret setup, not full Production acceptance. Existing
synthetic database/object-store volumes are preserved and reused. MinIO remains
512 MiB / one CPU; its earlier near-cap result is a reason to measure, not silently
increase the budget. No builds/full regressions should overlap this run.

Evidence lives under `/tmp/locintel-memory.kcSvWL/s3-mixed-pilot` and
`s3-mixed-30m`; sibling logs retain the same names with `.log`. Neither partial
progress nor a cold pilot establishes steady-state sizing. Report late memory
windows and object-store headroom separately before considering a smaller API cap
or requesting a longer experiment. The task follow-up will notify completion,
failure or required action and stay quiet otherwise; no automatic extra run.
The final result remains pending. The existing automation was updated to this
run, with verified temporary-runner cleanup instructions; it is not monitoring
the completed two-hour baseline anymore.

## Historical baseline scope

Static pre-generated image `6c48bb5adc90f232d6984a56a1f84d3aba108c9d250d89d010cc548ad5aaf783`,
API limited to 512 MiB / one CPU, separate worker, PostgreSQL, ClamAV, SSR and edge.
Development/local adapters remain explicitly synthetic; Production guards are
unchanged. Rate limits remain 300/user/minute and the default 600/org/minute.

Forty distinct local user sessions are provisioned through invitations and
sign-in, each with the Owner role in one org. This exercises real cookie-session
validation and gates, not full browser execution or mixed-tenant/role isolation.
The initial API-key pilot correctly received 401 from the user-only alert summary,
so the workload changed to sessions; authorization was not weakened.

Reads rotate over paged/search/detail sites, paged/search incidents, incident
statistics, alert summary, entities and cases. Synthetic incidents are seeded
through the API; sites/incidents are populated, while entities/cases may be empty.
The completed soak is 20 simultaneous requests every three seconds for 30 minutes,
with one incident write/minute, a clean ~1 MB upload/scan/download every two minutes,
guest SSR reads, a scanner reload, and three minutes cooldown. Existing synthetic
volumes are reused. This is a prototype-scale interactive workload, not an
enterprise dataset, continuous saturation, or a throughput ceiling.

## Before remediation: default configuration failed

One-minute stages at 1, 5, 10 and 20 clients passed. At 40 clients the first burst
returned 14 successes and 26 fifteen-second timeouts. Smaller reproductions also
failed. A prior 20-client pilot failed once; its repeat passed. No memory pressure,
OOM or rate-limit rejection accompanied the failures.

Independent probes corrected an initially suspected edge problem: both direct
and edge `/livez` remained responsive, but direct API `/healthz` returned 503.
PostgreSQL showed idle connections rather than an executing lock wait. The
application's default `Maximum Pool Size` is 20. The shared regional Npgsql data
source backs module contexts and readiness checks. Generated incident handlers
begin a module transaction before the gate queries a separate Identity context,
providing a hold-one/need-another connection starvation path under concurrency.

Changing only the API's native connection-string limit to 64 made the 40-client
pilot pass all 240 burst requests. This strongly supports pool starvation as the
cause. The override is private diagnostic configuration, **not a shipped fix**.
Increasing every replica's pool would consume the database connection budget and
does not by itself make arbitrary concurrency safe.

## Diagnostic pool-64 result

Only the API pool was raised to 64; the worker stayed at 20. The separate
40-client pilot passed 240/240 burst requests (p95 470 ms). The subsequent
30-minute soak used 20 concurrent sessions, followed by three minutes cooldown:

- 11,940/11,940 burst requests returned 200, approximately 6.6 requests/second.
  p50 51.4 ms, p95 173.9 ms, p99 328.8 ms, maximum 552.6 ms. These are client
  end-to-end timings through the edge, not just handler execution time.
- 12,406 counted successful driver requests including setup and auxiliary work;
  this is not a count of every HTTP exchange. All 991 independent readiness
  probes returned 200. No OOM, memory-limit events, or rate-limit rejections.
- Clean scans/download byte checks, EICAR quarantine/download refusal, scanner
  reload, writes and guest SSR assertions passed. The original session and
  stored file remained usable after soak; this run did not recreate containers
  before those assertions. Final durable dead-letter count: zero.
- API CPU averaged 6.0% of one core during soak. Cumulative cgroup CPU throttling
  was 14.25 seconds over the load phase; this is not a request timeout measure.

| Service | Limit MiB | Sampled peak MiB | End cooldown MiB |
| --- | ---: | ---: | ---: |
| API | 512 | 241.2 | 240.2 |
| Worker | 1,024 | 115.8 | 115.4 |
| PostgreSQL | 512 | 175.8 | 137.5 |
| ClamAV | 4,096 | 1,971.6 | 1,027.0 |
| Public SSR | 512 | 38.9 | 30.1 |
| Console/edge | 128 | 23.7 | 22.3 |

These are cgroup `memory.current` readings, not process RSS. API peak RSS was
331.8 MiB and PSS 272.5 MiB; shared pages/accounting make these different measures.
The scanner's kernel-recorded `memory.peak` was 2,069,336,064 bytes (1,973.5 MiB).
Combined sampled application memory peaked at 2,499.3 MiB (2.44 GiB) around reload
and ended at 1,572.6 MiB (1.54 GiB). Samples are near-contemporaneous, not atomic;
this excludes host OS, Docker VM/daemon, diagnostic runner and other host overhead.
It is not a host-sizing guarantee. AV separation prevents its roughly 1 GiB
steady / 1.93 GiB reload cost from being duplicated in each API replica, but does
not eliminate that cost or establish scanner scale-to-zero.

### Memory trend: headroom, not proof of a plateau

API five-minute median cgroup memory was 180.3, 203.9, 215.2, 222.1, 231.9 and
234.3 MiB. Anonymous memory medians rose from 85.2 to 123.5 MiB; file-backed
accounting rose from 90.1 to 105.6 MiB (including shared memory, not all reclaimable
file cache). RSS medians rose from 284.0 to 326.4 MiB. Growth slowed, but cooldown
did not restore the starting footprint. Do not label this leak-free or extrapolate
a fixed per-replica footprint indefinitely.

Managed committed memory peaked at 60.8 MiB and ended at 59.0 MiB; loaded
assemblies increased from 213 to 215 and then held. That does not resemble the
earlier Dynamic compilation footprint, but does not establish the cause of the
remaining anonymous-memory growth. Worker medians rose from 96.9 to 112.3 MiB;
its memory also remained allocated after cooldown. No forced GC was used.

## Acceptance order

1. Bounded HTTP admission and a failing-before regression are implemented; see
   verification above. Pool64 remains diagnostic evidence, not the fix.
2. Rerun the same default-configuration ramp and soak. Include concurrent writes
   and reads at the regression's saturation boundary; this workload was mostly
   reads with intermittent writes, not a maximum-capacity test.
3. After that passes, run a multi-hour steady workload with the same counters to
   resolve retained growth versus a warm plateau. Use bounded diagnostics if
   anonymous memory keeps climbing; retain hard limits and do not use restarts
   to hide growth. Keep 512 MiB API as a tested candidate, not an unconditional
   production guarantee.
4. Then validate replica/database budgeting and the previously outstanding
   Production-provider, backup/restore and hosted acceptance work. This local
   synthetic test does not authorize or establish deployment readiness.

Historical raw evidence: `/tmp/locintel-memory.kcSvWL/soak-*`, with `profile-soak.py`,
`soak-override.yml` and `summarize-soak.py` in the parent. Evidence is temporary,
private, and not a durable backup. Synthetic membership cleanup completed before
container teardown. All profile containers/network and the temporary SDK runner
were removed; host-side evidence, synthetic records and named volumes remain.
No product code, shipped pool setting, rate limit, or timeout was changed during
that historical baseline test. Documentation whitespace checks passed. Nothing was committed
or deployed.

Related: [memory investigation](memory-diagnostics.md),
[container staging](container-staging.md), [project overview](../README.md).
