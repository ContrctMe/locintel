# Transaction lifetime: objective review and targeted changes

Updated 2026-09-05. Owner: project maintainers. Status: targeted implementation
through priority 5 is in source and targeted validation passes; the new-image
two-hour soak completed. Dominant file growth is attributed to local-upload page
cache; bounded automatic reclaim and local S3 controls pass. Representative warm
production-storage sizing and causal lifetime measurement remain.
Preview publication retains an intentional transactional exception.

## Conclusion

Keep the modular monolith, per-module contexts, shared regional data source,
tenant isolation and durable outbox. The evidence does not justify replacing
those designs. It does justify narrowing transaction scope: automatically eager
transactions are unnecessarily broad for the reviewed reads, and particularly
costly when handlers await external I/O. HTTP admission protects capacity but is
not a substitute for correcting those lifetimes.

The objective is the shortest **correct** unit of atomic work, not the fewest
milliseconds at any cost. More transaction boundaries can add round trips,
partial commits and retry complexity. A long-lived DbContext need not retain a
connection; an explicit open transaction does. Returning a connection to its
pool is desirable; destroying the reusable physical connection after every query
is not the goal.

## Evidence and decisions

| Path | Observed behavior | Decision |
| --- | --- | --- |
| Incident/site lists and reviewed dashboard reads | Eager module transaction starts before gates, remains during queries/processing and cross-context lookups | Explicitly opt reviewed handlers out of automatic transactional middleware |
| Incident report and other writes | Incident report now queues audit/messages before its single final save | Native Lightweight mode on this POST only; other writes retain their existing boundaries |
| Scanner | External read/scan now runs without a transaction; completion rereads and locks the file | Implemented conditional, atomic completion; see verification below |
| Derivative handler | Source reads are connection-free; final preview write and metadata update retain a row lock | Serialize with erasure; deterministic cleanup handles failed metadata commits |
| Connector sync | Audited credential attempt commits before connection-free KMS/HTTP work | Locked, conditional completion atomically stages the batch and updates sync time |
| Generated incident POST | Body parsing occurs without an endpoint transaction | Invalid JSON starts no transaction; injected commit failure leaves no incident/audit |
| Auth callbacks and custody-sensitive GETs | Signup and evidence issuance are now POST-only; protocol callback and incidental view audit remain | No blanket GET convention; issuance does not prove completed delivery |
| Aggregate projections | Transaction-scoped advisory locks serialize aggregate materialization | Keep locks, mutations and outbox in one transaction |

Relevant implementation:
[incident endpoints](../src/Modules/LocIntel.Modules.Incidents/Incidents/IncidentEndpoints.cs),
[storage pipeline](../src/Modules/LocIntel.Modules.Storage/Pipeline.cs),
[connector sync](../src/Modules/LocIntel.Modules.Ingest/ConnectorSync.cs),
[custody download](../src/Modules/LocIntel.Modules.Cases/Cases/CaseEvidenceEndpoints.cs),
[aggregate lock](../src/LocIntel.Platform/Data/AggregateLock.cs),
[audit capture](../src/LocIntel.Api/AuditComposition.cs).

### Important corrections to overly broad advice

- An explicit default `READ COMMITTED` transaction does **not** give list count
  and page queries one fixed snapshot. PostgreSQL takes a new snapshot for each
  statement. The reviewed reads had no stronger explicit isolation or row locks;
  their transaction did not provide the consistency benefit sometimes assumed.
  If a particular response needs a coherent snapshot, specify and test that
  requirement rather than pretending the previous transaction supplied it.
  [PostgreSQL isolation documentation](https://www.postgresql.org/docs/current/transaction-iso.html).
- Authorization queries use a separate Identity context. Merely keeping the
  module's transaction open does not atomically couple grants to the mutation.
  Critical write checks need an explicit concurrency policy; moving them without
  analyzing that policy is not a universally safe optimization.
- Switching every handler to Lightweight is unsafe for the present write shape.
  That mode relies on `SaveChangesAsync` boundaries, so existing explicit saves
  and later publications require review. It also cannot replace the transaction
  protecting bulk SQL or advisory locks. This is why the global mode is unchanged.
  [Wolverine transaction modes](https://wolverinefx.io/guide/durability/efcore/transactional-middleware.html).

## Implemented read scope

Native `[NonTransactional]` replaces the eager attribute on these seventeen reviewed
handlers; it opts out of `AutoApplyTransactions`, rather than introducing custom
transaction middleware:

- Incidents: list, detail, statistics.
- Sites: list, open-now, detail, schedules, windows.
- Entities: list only; sensitive detail audit behavior is not changed.
- Cases: list only; custody and package flows are not changed.
- Alerts: list and summary; marking read remains transactional.
- Storage: file list and detail (not completion or evidence issuance).
- Ingest: batch list, batch preview and connector list.

Their bodies and gate/filter/RLS logic are unchanged. Authorization and access
audit still use the durable asynchronous path; there is no domain mutation to
commit with these reads. No automatic opt-out based merely on the HTTP verb was
introduced. Write attributes, admission limits and pool budgets are unchanged.
The existing CLAUDE rule about declaring a transaction owner was clarified to
apply to transactional handlers, rather than inadvertently forcing all reads
to have a transaction.

## Verification

`dotnet test tests/LocIntel.IntegrationTests --filter FullyQualifiedName~ReadTransactionTests`
is the focused resource-lifetime check. It observes EF transaction events during
real authenticated HTTP requests against real PostgreSQL, not a mock pool.
The initial incident-list check failed with expected zero / actual one transaction
in a one-second test body; it passed after opting out. The expanded check failed
on incident detail before the remaining changes. It also creates an incident and
asserts a write transaction was observed, so a disabled observer cannot pass it.

The earlier read-only phase passed all three cases: the original incident-list check
and the expanded twelve-path check in both Dynamic and Static loading modes.
Existing incident/audit/admission checks passed with the initial expanded check
(18 cases); twenty additional entity/case/alert/gate/concurrency/paging/schedule
cases passed. All 60 architecture tests passed. The Release build completed with
zero warnings/errors, and fresh handler generation succeeded. No new full
integration or browser suite is claimed.

This proves elimination of the observed endpoint transaction, not a measured
latency or RAM improvement. The already-running two-hour soak uses the earlier
admission-only image, not these source changes. Small, separate-database tests
and local builds overlapped part of that run; account for host contention when
interpreting its timings. The soak image and data were not replaced.

## Approved implementation order and acceptance

1. **Storage scanning/derivatives:** read a tenant-scoped, stable work description,
   release the connection, perform external I/O, then use a short transaction to
   conditionally apply the result plus audit/outbox. Revalidate that the file is
   still eligible; do not resurrect erased or superseded work. Test duplicate
   delivery, concurrent state changes, scanner failure/cancellation and crash
   recovery. Never treat scanner failure as a clean result. No new queue/service
   is necessary merely to shorten the transaction, but message completion and
   result persistence must remain recoverable together.
   **Progress:** scan separation and connection-free preview reads implemented.
   Final preview publication deliberately retains a row lock; see the exception below.
2. **Connector sync:** use the same bounded approach while retaining credential
   audit, source configuration validation and staging semantics. Test remote
   timeout, changed/deleted connector, duplicate completion and atomic batch save.
3. **Write preparation:** prove one narrow write pattern before propagating it.
   Parse/validate before opening the transaction; retain race-sensitive checks
   and atomic data/change-audit/outbox work at commit. Prefer supported Wolverine
   mechanisms; do not build an ambient cross-module transaction manager.
4. **GET action semantics:** change signup provisioning to an explicit POST and
   evidence download-access issuance to POST. Record issuance, not completed
   download; a signed URL does not prove delivery. Update callers/contracts and
   test authorization, CSRF protection, retries and absence of action on GET.
   Keep incidental entity-view audit and the protocol authentication callback;
   GET safety is not a prohibition on all logging or authentication side effects.
   The console will submit signup as a native POST form (email in the body),
   followed by a 303 redirect into the authentication flow. Cross-origin signup
   is refused even without a session cookie. Evidence access remains a typed
   mutation; new custody events distinguish URL issuance from historical
   `Downloaded` entries rather than rewriting historical records.
5. **Expand reviewed read coverage**, then benchmark the new image separately
   with connection occupancy/hold-time and acquisition-wait measurements. Keep
   admission until mixed writes/background work prove a different budget safe.

These changes require workflow-level failure tests, not mechanical attribute
replacement. None establishes a need to replace EF, Wolverine, or the modular monolith.

## Current implementation details and remaining limits

### Preview generation and erasure

The orchestrator reads eligible source bytes outside a transaction, then invokes
`SaveFilePreview` with at most 4 KiB. The completion rereads the file under
`FOR UPDATE`, checks Clean status, immutable key and absence of a published preview,
then writes the deterministic preview key and records its metadata. Delete,
restore, expired-trash erasure and organization purge serialize on the same row.
Shared erasure deletes the deterministic preview even when a failed metadata
commit left `PreviewKey` null; legacy distinct preview keys are also removed.

**Intentional exception:** the final object write still holds a connection and
row lock. Four KiB bounds bytes, not network latency. Moving it outside the lock
without durable write/erase coordination can recreate bytes after erasure. Keep
this simple correct boundary until measured object-store latency warrants that
additional protocol. Existing erasure I/O remains transactional too.

### Connector synchronization

The orchestration commits a credential-access **attempt** audit before decrypting
credentials or contacting the remote endpoint. Failed remote work therefore does
not erase that record. No storage/ingest connection remains open during the HTTP
probe. Completion locks the connector, rejects changed/deleted configuration or
a stale sync snapshot, and stages the batch plus sync timestamp atomically.
New work carries a serialized UUIDv7 batch ID; replay after commit skips another
fetch and batch. Concurrent snapshots are coalesced by the sync-time fingerprint.

Deployment caveat: queued legacy `SyncSiteConnector` payloads lack this batch ID.
Drain those before rollout; the initializer does not supply a stable identity
across separate deserializations of an old payload. Connector response size is
not newly bounded by this change. Attempt audit may repeat on failed retries;
this is intentional, not exactly-once remote execution.

### Write preparation and HTTP action semantics

Only incident creation adopts native Lightweight middleware: its early explicit
save was removed, and audit/outbox work is queued before the middleware's final
save. Bulk SQL, advisory-lock and other write handlers were not mechanically
converted. Cross-context authorization was not made atomically consistent by the
old transaction and is not claimed to be so now.

Signup uses a native form POST with validated email in the body and a 303 redirect.
Supplied foreign origins are rejected even without a session cookie; native
clients without Origin retain the existing policy. Evidence access issuance is
POST-only with authorization, CSRF and idempotency checks. New custody records use
`DownloadAccessIssued`; historical `Downloaded` records are preserved. These verb
changes require callers to update together. OpenAPI and generated client types
were regenerated; callback GET and incidental entity-view audit are unchanged.

### Current validation ledger

- Preview/storage/trash/offboarding batch: 21 passing cases, including a failed
  metadata commit, deletion during source read and purge racing publication.
- Connector checks: four passing cases covering connection release, commit
  failure/retry/replay and changed/deleted/failing remote work; existing ingest
  checks also passed in the seven-case batch.
- Incident write-preparation plus existing incident checks: four passing cases.
- HTTP contracts, signup/CSRF, case custody and added read probes: seven passing cases.
- Regenerated TypeScript contracts typecheck; 30 console and 11 public tests pass.
- Combined final integration: 39 passing cases. An additional Static-loading
  run passed 22 cases; architecture passed all 60. Fresh generated handlers were
  inspected: incident creation parses JSON before its single final save without
  an eager transaction. Formatting and `git diff --check` pass.
- Both new Chromium browser checks passed (signup POST/redirect and evidence
  POST/custody issuance). The first isolated-port attempt failed
  before browser execution because launch settings forced API port 5293; both
  harness launches now disable launch profiles so explicit ports take effect.
  The rerun passed with custom ports; no full browser suite is claimed.
- New-image CPU/RAM/connection measurements remain outstanding; the running
  admission-only baseline is not evidence for this source revision. The new
  Linux ARM64 artifact is built and locally loaded as
  `locintel:transaction-lifetimes`, image
  `sha256:8aa6d4d8ab65741868e0027df009e76ba23289441857abdbb756c1db6345b63b`.
  Archive: `/tmp/locintel-transaction-lifetimes.tar.gz`. No registry push or
  replacement of the active baseline containers occurred. The matching console
  is now built as `locintel-console:transaction-lifetimes`, image
  `sha256:f537076548a9a950d4f644ad7caec2033ba9e3b2c0405f803e6ef29572e78b52`.

### Broader regression and comparison preparation

The two new flows also pass in Firefox (49.7 seconds) and WebKit (36.3 seconds),
using fresh isolated stacks. This is cross-engine validation of those flows,
not the full browser suite. The bounded console builder was stopped after build.

The full integration rerun reported 316 passes, two failures and one skip before
the test host aborted at about ten minutes. It is not a completed green suite.
Log: `/tmp/locintel-full-regression.log`. No new OS crash report was found; the
abort cause is unconfirmed. Local builds/browser tests overlapped, and a bounded
EF trace was attached midway (after the collectibility failure), so a clean
follow-up should use the existing CI sharding and retain TRX/blame diagnostics.

- `IncidentImportTests` staged immediately after upload completion, racing the
  asynchronous scan. Its helper now awaits Clean with the existing bounded wait;
  the unchanged staging assertions pass in the focused rerun. Product rejection
  of Uploaded files remains intact.
- `HostLifetimeTests.Disposed_hosts_are_collectible` failed the existing one-second
  GC check in the full Dynamic suite and in a separate Dynamic test process.
  The Static-mode rerun passed (19 seconds); this is the container profile mode.
  A subsequent reduced-contention Dynamic rerun also passed (17 seconds), so
  this is not established as a mode-specific leak. Failure diagnostics identify which
  host/provider/limiter weak references survive, without extending the timeout
  or retaining their targets. It also passed in the subsequent second CI shard
  (52 seconds). The earlier under-load failure is not explained or erased by reruns.
- Shard 1 additionally reported HTTP 503 on the initial API readiness assertion
  in `DependencyReadinessTests` (`WorkerRoleTests.cs:55`). Its failure does not
  establish whether startup timing or an unavailable dependency is responsible;
  readiness behavior was not relaxed to force a pass.

Final sequential-shard results: **326 passed, one failed, one expected scale skip**
across 328 discovered cases. Shard 1 recorded 155 passes and the readiness failure;
all 156 tests finished, but the runner did not exit. The five-minute inactivity
watchdog captured a hang dump and aborted that host. Shard 2 recorded 171 passes
and one skip and exited normally. The import fix passed in shard 1; collectibility
passed in shard 2. All 51 platform unit tests also passed. This is not an all-green
regression result: readiness and post-test runner/teardown reliability remain open.

The hang dump shows the VSTest/xUnit runner waiting after test completion; the
minidump cannot resolve the required async heap types, so it does not establish
which application or runner continuation is responsible. Do not infer a product
memory leak, or an OOM, solely from this artifact. No new production code was
changed to mask either failure. Next diagnostic work should capture the failed
readiness response body and a full heap dump if the post-test hang recurs.

Evidence:

- `/tmp/locintel-regression-shard1.log`, `/tmp/locintel-regression-shard2.log`.
- `tests/LocIntel.IntegrationTests/TestResults/shard-1-51396/integration-shard-1.trx`.
- `tests/LocIntel.IntegrationTests/TestResults/shard-2-51394/integration-shard-2.trx`.
- Hang dump under `TestResults/shard-1-51396/7c99e378-9edc-4215-98e6-be1c8de266aa/`;
  native stacks at `/tmp/locintel-shard1-dump-stacks.log`.

#### Readiness/hang diagnostic follow-up

The isolated `DependencyReadinessTests` case passed and exited cleanly in 17 seconds.
The exact 47-class set from the failed shard's TRX was then replayed three times:
all **156/156 passed** on each run (2m29s, 2m58s, 2m34s), with normal test-host
exit. These are repeated observations of the same scope, not 468 distinct tests.
Each replay retained the existing five-minute hang timeout and enabled full dumps;
none hung, so no new full dump was produced. The earlier minidump remains preserved.

Neither the readiness failure nor the post-test hang reproduced. Consequently,
their root causes and any causal relationship remain **unconfirmed**. Startup
timing, dependency contention and fixture/runner cleanup are possibilities, not
findings. Do not close these issues as fixed or change production readiness based
only on the successful reruns. No production code, readiness criteria, pool budget
or soak configuration was changed for this investigation.

Failure-only test diagnostics now report the initial readiness response body,
bootstrap readiness flag, cancellation flag and durable queue/listener states.
These snapshots are taken after the response and cannot identify every transient
database failure. A clean response still requires success immediately; no readiness
retry or longer timeout was added. The shard runner accepts an opt-in full-dump
setting, with its default minidump and timeout unchanged:

```sh
HANG_DUMP_TYPE=full tools/run-integration-shard.sh 1 2
```

The shell option was checked for both default `mini` and explicit `full` forwarding;
shell syntax, C# formatting and diff-whitespace checks pass. Full dumps may contain
sensitive heap contents and consume substantially more disk; keep them local or
in restricted diagnostic storage, not ordinary public CI artifacts. On recurrence,
retain the readiness snapshot and full dump together before attempting a fix.

Diagnostic artifacts:

- `/tmp/locintel-readiness-diagnosis/readiness.trx`.
- `/tmp/locintel-shard-diagnosis/shard.trx`.
- `/tmp/locintel-shard-diagnosis-2/shard.trx`.
- `/tmp/locintel-shard-diagnosis-3/shard.trx`.
- `/tmp/locintel-shard-diagnosis.log` and `/tmp/locintel-shard-diagnosis-repeat.log`.

Comparison preparation is at
`/tmp/locintel-memory.kcSvWL/comparison-preparation.md`, with
`comparison-override.yml` and opt-in `--comparison` support in the existing
`profile-admission.py`. Compose validation and Python compilation pass; the
driver's refusal to replace the active baseline was exercised successfully.
The read-only PostgreSQL activity query also executed successfully.
The default path used by the already-running baseline remains unchanged.

The comparison collects native Npgsql occupancy/pending-request/timeout counters
alongside runtime metrics, plus sampled PostgreSQL transaction ages/wait states.
Npgsql does not expose a pool-wait-duration histogram: creation time and command
duration must not be relabeled as pool wait.
[Npgsql metric implementation](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/MetricsReporter.cs).

A temporary 60-second, 64-MiB-buffer EF diagnostic collector selects only lifecycle
IDs/timing fields; no SQL, credentials or entity values are selected. Its test-host
smoke capture and reader verified 4,049 connection-open and 1,305 transaction-start
events with populated IDs and elapsed milliseconds. Container correlation and counter availability
still require a pilot. Connection-open elapsed time includes physical connection
creation, not just queue wait; sampled transaction ages are lower bounds, not
completed lifetime percentiles. EF events omit direct Npgsql operations, while
native pool counters cover them. The comparison notes specify trace-boundary and
instrumentation-overhead caveats and require a similarly instrumented short
baseline before causal hold-time comparisons. No production instrumentation or
application dependency was added.

## Scan implementation and verification

### New-image pilot (2026-09-05)

The container pilot completed 511 counted successful requests, including 400 soak
requests (20 clients for 60 seconds), with all 58 readiness probes returning 200,
zero dead letters and no memory-limit/OOM events. Real ClamAV clean/EICAR checks
passed. Soak p95 was 309.6 ms; sampled API peak was 159.2 MiB. This short,
instrumented pilot is not a steady-state footprint or a causal speedup claim.
Evidence: `/tmp/locintel-memory.kcSvWL/transaction-pilot`.

Native regional pool metrics were present: max 20, sampled used 0–1 and idle 2–10.
Two-second samples miss short occupancy peaks. Pending/timeout series were absent,
so absence must not be reported as measured zero. PostgreSQL activity was sampled
31 times. The 60-second EF trace contained 2,351 connection-open/close events each
and 15 transaction-start/commit events each, with populated IDs and UTC ticks.
The reader verified payload availability; equal counts alone do not prove paired
lifetimes. No percentile claim is made without correlation and boundary handling.
The diagnostic runner is separate and capped at 512 MiB / 0.5 CPU.

A separate two-hour run has started with the same API cap, pool/admission budget and
workload. Existing synthetic volumes are reused, so accumulated fixture data and
baseline host contention preclude a controlled causal latency comparison. Avoid
overlapping builds/full regression suites. Keep the admission-only final results
in [the soak report](concurrency-soak.md) distinct from this image's measurements.
Evidence: `/tmp/locintel-memory.kcSvWL/transaction-pool20-2h`; log
`/tmp/locintel-memory.kcSvWL/transaction-run.log`. The run subsequently completed
49,698 counted successes, including 47,560 soak requests, all 3,819 health probes,
zero dead letters and no OOM/limit events. Soak p95 was 186.0 ms; sampled API peak
317.0 MiB, ending 315.3 MiB. Native pool used connections peaked at 10/20 in
two-second samples. PostgreSQL's oldest sampled transaction age was 3.688 ms,
not a completed lifetime percentile. Pending/timeout series remained absent.
Continued file-backed growth prevents a plateau claim. The detailed final
memory/CPU ledger and ordered remaining work are in the linked soak report.
Subsequent artifact replay matched every inactive-file increase to a page-rounded
upload, and two short no-collector controls released/reloaded those exact files'
cache with byte checks intact. This is discardable local-upload cache, not evidence
of managed-buffer retention. No cache flushing was added to product code; automatic
reclaim was subsequently proven in a bounded pressure control (75.8 MiB reclaimed,
no OOM, nine passing health probes). A local S3 control transferred 97.4 MiB with
zero API inactive-file growth, while MinIO carried the storage/cache footprint.
Residual warm-memory behavior and mixed-load production-storage sizing remain
separate questions; the short S3 result does not justify reducing the API cap.
Containers and the temporary comparison runner were removed, evidence and volumes
retained, and follow-up paused. No additional experiment or deployment was started.

The first probe reproduced an open transaction during AV scanning. Selective
Lightweight mode released the connection, but a second regression proved that
this alone could overwrite an intervening erasure with a late Clean result.
That intermediate implementation was replaced, not shipped as the solution.

`ScanUploadedFileHandler` now performs a tenant-filtered, untracked read and the
object read/scan without a transaction. It invokes `ApplyFileScan` inline through
Wolverine, with the tenant explicitly propagated. The completion handler opens
the normal eager transaction and rereads the file using PostgreSQL `FOR UPDATE`.
It accepts only a still-Uploaded file with the same immutable object key, then
saves the state and derivative/quarantine outbox work together. This retains
tracked EF changes and audit integration; it does not use bulk SQL to bypass them.

Concurrent completions serialize on the row; subsequent ones see an ineligible
status. A failure before completion commits leaves Uploaded available for retry.
If commit succeeds but the original scan delivery is retried before acknowledgement,
the status guard prevents duplicate completion effects. No new service, queue,
schema migration, global transaction setting or pool-budget change was added.
This is recovery by safe retry, not a claim of exactly-once scanner execution.

Verification in Dynamic mode: 20 scan/storage/audit integration cases passed,
including five focused checks for connection release, concurrent erasure,
duplicate completion/tenant isolation, cancellation/retry and injected commit
failure. The commit-failure test throws at the actual transaction commit after
the update/outbox save, verifies Uploaded survives, and retries successfully.
All 60 architecture tests passed. Fresh code generation succeeded; eight existing
storage cases and all five focused scan cases passed under Static loading.
The final lifetime probe observes actual storage connection open/close events,
independent of generated dependency construction, and verifies the observer ran.
These tests use real PostgreSQL and local object storage;
the lifetime probe substitutes the external scanner. Existing local clean/EICAR
flows passed; no fresh real-ClamAV, process-kill recovery, full-suite or performance
run is claimed for this change.

Implementation: [scan orchestration](../src/Modules/LocIntel.Modules.Storage/Pipeline.cs),
[conditional completion](../src/Modules/LocIntel.Modules.Storage/ApplyFileScanHandler.cs),
[regression checks](../tests/LocIntel.IntegrationTests/ScanLifetimeTests.cs).

Related: [concurrency/soak evidence](concurrency-soak.md),
[memory diagnostics](memory-diagnostics.md),
[persistence ADR](decisions/0017-dbcontext-schema-per-module.md),
[project overview](../README.md).
