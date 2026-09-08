# Container staging preparation

Status: active, not deployment-ready. Updated 2026-09-05. Owner: project maintainers.

## Agreed scope

Use LocIntel as the production-shaped sample for the Premise fork. First update
the fork, then prove containerization locally before choosing or purchasing a host.
The intended starting topology is one always-on VM with Docker Compose, one API,
one worker, a one-shot migrator, PostgreSQL, the console static server, the public
SSR server, a scanner, and an HTTPS reverse proxy. No Kubernetes or HA requirement.

Staging is invite-only with synthetic data; no real people/evidence or commercial
launch is assumed. SMS and paid AI stay off. Target roughly $20/month all-in;
$25–30 is worth considering for a meaningfully better experience. No provider,
region, account purchase, remote push, or hosted deployment is authorized by this
local proof. Treat costs as a baseline, not an excuse to weaken safeguards.

## Fork update

Work branch: `codex/container-staging`, based on LocIntel `23af13b`.
Upstream source: Premise `e1b15528a181a1d9def9145be5e4ba6b5783ca63`, the verified
remediation branch commit, not an assertion that those changes are on upstream main.
The normal sync script advanced `template-renamed` to `248a1eb`; preserve that
snapshot ancestry for later updates. The merge is not yet committed.

Resolutions retain all seven product modules and their routes, optional providers,
and the actor-aware gates that product writes actually use. The combined backend
regenerates OpenAPI and TypeScript types. Product calls now use contract paths,
explicit path/query parameters, checked enum selections, and query cancellation.
No permissive legacy client overload was introduced.

The stricter contract exposed a real schema-name collision: patrol scheduling and
site hours both named their request `CreateScheduleRequest`. Patrols now use
`CreatePatrolScheduleRequest`; the HTTP body still has `startLocal`. An integration
assertion checks that patrol scheduling does not acquire the site-hours `opens`
field. The weekly schedule helper's `rRule` spelling is also consumed correctly.

## Evidence so far

- Architecture suite: 60 passed.
- Platform unit suite: 51 passed, including the retained actor-gate tests.
- Frontend unit suites: console 30 and public 11 passed.
- Full frontend typecheck and lint: passed after the client migration and cleanup.
- Console and public SSR production builds: passed. The public tips route still
  reports an existing TanStack `inputValidator` deprecation warning.
- Combined OpenAPI snapshot plus patrol schema regression: passed.
- Full integration suite: 298 passed, two failed, one explicitly opt-in scale
  baseline skipped (301 total; 8m13s). One failure was the SMS Production refusal
  message; its explicit refusal was corrected and all 35 boot-guard cases passed.
  The other was a marketplace vendor-directory projection timeout; both tests in
  that class passed in an isolated rerun (8s). No projection fix was made or
  claimed. This is not an all-green full integration result.
- Chromium: 35/40 passed in the broad run while integration tests and image work
  overlapped. CSV preparation and four session tests exceeded their existing
  waits. All five passed on a fresh isolated stack in 15.9s, without changing
  timeouts or retries. Load sensitivity is a hypothesis, not an established cause;
  no timing issue is declared fixed. Firefox/WebKit have not been run for this fork.
- Backend OCI image `locintel:staging-local`: built; migrate/API/worker smoke
  passed with pre-generated handlers, non-root serving roles, readiness/liveness,
  and durable worker cleanup. It precedes the SMS error-message correction. The
  configured cloud provider endpoints were inert: this does not validate provider
  calls, persistent key storage, frontends in containers, or the complete topology.
- Failure evidence is preserved locally at `/tmp/locintel-sync-evidence.tV8G88`
  (browser traces/stack logs and the original integration TRX). Temporary storage
  is not a durable backup or a repository artifact.
- No full-stack container, resource-sizing, backup/restore, or hosted acceptance
  result has been established by these checks.

## Acceptance order still to complete

### Sequential verification follow-up (2026-09-05)

The rebuild passed without warnings/errors. Two all-in-one integration attempts
aborted after 181 passes + one skip and 209 passes + one skip, respectively.
Neither is a passing suite. The four classes active during the first termination
passed in isolation (22 tests, 19s, normal exit).

The diagnostic second run establishes an OS memory-pressure termination:
macOS logged `killing largest compressed process dotnet [78531] 38606 MB` at
09:03:19 local time. Crash capture was enabled but produced no dump. The source
of memory growth remains unproven; this is not evidence of a production-service
leak, nor a reason to weaken test assertions or timeouts. Existing CI already
uses two integration processes; both partitions passed when run sequentially.
Shard 1/2 passed 122 tests (2m40s); shard 2/2 passed 178 with one opt-in skip
(2m50s). Both exited normally: all 301 discovered tests accounted for, 300 passed.
This validates the existing sharded workflow, not the all-in-one memory behavior.

Fresh full Chromium verification passed 38/40 (1.5m). Role-editor setup and
site-hours sign-in setup failed; preserved traces show HTTP 429s, including
`/api/roles` and `/me`. The suite reuses seeded identities across tests, so
cross-test rate-budget consumption needs isolation. No rate limits or waits
were raised. All five previously failing overlapping-run scenarios passed.
Both failed scenarios passed on a fresh isolated stack (2/2, 8.4s), supporting
shared-suite rate-budget interference rather than a consistently broken editor.
This is not a passing complete browser suite. The script stopped before
Firefox/WebKit; those engines remain unverified. The sync remains uncommitted
and Compose work had not started at that checkpoint (see the subsequent work below).

Evidence: `/tmp/locintel-sequential-evidence.pqKbHR` contains both aborted reports,
the narrow rerun, runner diagnostics, and the exact kernel-log observation.
This is local temporary diagnostic storage, not a durable backup.

### Remaining steps

Bounded-memory diagnosis is now captured in [memory diagnostics](memory-diagnostics.md):
the full runner hit its 4 GiB cap; repeated disposed fixtures retained complete
hosts; a separate five-minute API/worker workload completed under independent
limits. The lifecycle fix now passes collection checks for hosts and both limiter
timers; all 303 integration cases completed under 4 GiB (302 passed, one skip,
no OOM). Architecture 60/60 and platform unit 51/51 also pass. Headroom is still
tight. The subsequent Dynamic full-stack local profile passed 778 measured requests,
real scanner reload/quarantine, and session/file persistence after recreation:
3.55 GiB sampled combined peak, 2.73 GiB cooldown, no pressure/OOM events with
the 2 GiB API cap. A later same-image comparison isolated runtime compilation:
Static passed 187 requests at a 512 MiB API cap with peak RSS 254 MiB, compared
with Dynamic's 1,722 MiB. The local profile now uses Static/512 MiB for API.
All 36 boot guards, 60 architecture tests, and the Production-mode three-role
Static image smoke pass. The subsequent [concurrency/soak report](concurrency-soak.md)
records default-pool starvation and a passing diagnostic pool-64, 30-minute soak:
11,940 workload requests, 174 ms p95, API peak 241 MiB cgroup / 332 MiB RSS.
Combined memory peaked at 2.44 GiB during scanner reload and ended at 1.54 GiB.
Bounded HTTP admission now passes the small-pool regression and the default-pool
40-client ramp (480/480 requests, 237 ms p95). The default-pool two-hour soak is
running; longer-term memory acceptance is still pending. The larger-pool numbers
above remain historical diagnostics, not the shipped fix.

`compose.profile.yml` and the two targets in `web/Dockerfile` prepare a local-only
profile of PostgreSQL, migrator, API, worker, real ClamAV, public SSR, and a combined
console/edge server. Only the edge port is exposed, on loopback. The backend uses
explicit Development/local adapters; this configuration must never be hosted.
Memory/swap and CPU limits apply to each service; restarts are not automatic so
an OOM stays visible. ClamAV has 4 GiB to cover signature reload, not just idle.
The images build successfully; Caddy validates as non-root with no capabilities.
The initial 1 GiB API budget OOM-killed during upload/browser warmup. The follow-up
used 2 GiB for API; do not erase that failed sizing evidence. The newer strict
pre-generated profile needs only a 512 MiB API ceiling for the measured workload.
Full workload, comparison, and restart results are recorded in the memory report.

### Local image reproduction

Use an isolated source copy while the template merge remains in progress. Restore
and build Release, run the existing `--codegen write` command, then use the SDK's
`DefaultContainer` publish profile with `ContainerImageTag=memory-profile`.
For the Alpine ARM64 diagnostic runner, explicitly pass `--os linux --arch arm64`;
implicit platform inference produced an unsupported `linux-musl` image target.
`ContainerArchiveOutputPath` allows exporting an archive for local `docker load`
without pushing to a registry. The runner itself must stay memory/CPU bounded.

Build the web targets from the `web` context:

```sh
docker buildx create --name locintel-profile-build --driver docker-container --driver-opt memory=3g,memory-swap=3g,cpu-quota=200000,cpu-period=100000,restart-policy=no
docker buildx build --builder locintel-profile-build --load --target public -t locintel-public:memory-profile web
docker buildx build --builder locintel-profile-build --load --target console -t locintel-console:memory-profile web
docker buildx stop locintel-profile-build
docker compose -f compose.profile.yml config --quiet
docker compose -f compose.profile.yml up -d
docker compose -f compose.profile.yml down
```

The named builder used here is a temporary `docker-container` builder with 3 GiB
memory, no swap, and two CPUs; create it explicitly before these commands, and
stop it before profiling. Do not change the user's default builder. Runtime
containers are a separate budget. `down` preserves the three named volumes
(database, app data/keyring, scanner signatures); do not use `down -v` unless
intentionally discarding that local synthetic state. Open the console at
`http://localhost:18080` and public locator at `http://acme-dev.localhost:18080`.

### Acceptance still open

Immediate follow-up: isolate browser test identities/rate budgets without
raising production limits, then rerun the complete browser matrix. The repeated
fixture investigation and bounded integration rerun are complete. Do not
use the test-host's 38,606 MB observation to size the deployment VM. These new
browser findings have not been accepted as staging risks or declared fixed.

1. Finish the fork verification: integration/browser checks, generated-artifact
   consistency, final diff review, and finish the merge without losing product
   changes. Text conflicts are resolved/staged; the merge remains uncommitted.
   Preserve any failure evidence before retrying.
   Run the complete suites without overlapping heavyweight local jobs before
   deciding whether the observed timing failures require deeper investigation.
2. **Local image/roles completed.** The existing smoke test uses inert provider
   settings; it is a role/boot check, not a working-provider acceptance test.
3. **Local topology/workload completed**, with both frontends and real scanning.
   Full tenant-isolation/browser acceptance and deliberate worker-only backlog
   drain remain; successful ordinary message processing is not that proof.
4. **Session/keyring and file persistence passed after recreation.** Next prove
   backup and restore on a fresh database/object store. A local restore rehearsal
   is not an off-server backup.
5. **Idle/load/reload/cooldown baseline completed.** Next measure a longer
   production-shaped soak, signature updates and deployment overlap; reserve
   aggregate host headroom. The earlier Dynamic topology did not fit 2 GiB total.
   Its host-sizing conclusion must not be carried forward unchanged after the
   large Static API reduction; measure the revised topology before buying a host.
6. Agree on provider/region and recurring cost including backups, storage, email,
   key management, and scanner operations; then obtain deployment approval.
7. Perform live-provider acceptance and controlled staging soak. The previously
   unreproduced Premise reliability findings remain accepted staging risks, not
   proven fixes or production readiness.

## Related context

- [Project overview](../README.md)
- [Product blueprint and decision log](product/crime-intelligence-blueprint.md)
- [Production configuration and boot guards](production.md)
- [Operations runbook](runbook.md)
- [Inherited platform maturity review](software-maturity-review-details.md)
