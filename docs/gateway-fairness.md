# Gateway fairness implementation and acceptance

The accepted direction is [ADR 54](decisions/0054-gateway-operational-fairness.md).
Request limits protect availability and fair access; they are not subscription
entitlements. This document distinguishes the intended contract from verified
implementation. Status: reference topology and functional controls pass locally;
small-data 1000/sec and larger mixed 250/sec short runs pass. General production
capacity, hours-long stability and cross-host scale efficiency remain unqualified.

## Existing identity behavior

| Request | Current authority | Implication for gateway classification |
|---|---|---|
| User session | Encrypted `locintel_session` cookie plus live `identity.user_sessions` revocation check | Use the application's cookie handler and session validation; never a raw tenant header |
| Tenant switch | `/auth/switch-org` issues a changed session cookie | Cache by credential, not user; reclassification follows the new cookie |
| Contact | Encrypted cookie with contact ID and active organization | Include contacts in the tenant partition, without requiring a user ID |
| API key | SHA-256 lookup of an opaque `locintel_` secret; revocation and expiration checked | A gateway cannot derive its tenant from the token format; reuse the application lookup |
| Cookie plus API key | An authenticated cookie takes precedence in the existing middleware | Preserve that precedence in classification and execution |
| Guest | Application host-to-organization lookup; no authenticated tenant identity | Use trusted client IP for abuse protection; guest cookies must not mint unlimited new buckets |
| Operator impersonation | Application cookie plus existing impersonation checks | Charge the active tenant for fairness without conveying authorization through gateway metadata |

Sources: `AuthenticationHosting`, `SessionValidationMiddleware`,
`ApiKeyAuthenticationMiddleware`, `RequestPrincipalAccessor`, and
`SessionContextMiddleware` in `src/LocIntel.Api`.

## Replacement contract

1. Client traffic reaches a gateway; application and identity-check listeners are
   private. Public requests cannot invoke the private lookup or forge its result.
2. Early IP/connection protection bounds work before identity resolution.
3. An authenticated partition identifies the active organization across users,
   contacts, and API keys. A user without an active organization gets an explicit
   user partition. Anonymous traffic uses a trusted client-IP partition.
4. Classification may be briefly stale, but may never authenticate or authorize
   the business request. Application checks remain authoritative on every call.
5. Rate and burst configuration belongs to operations. A tenant override changes
   policy without creating a new identity or resetting its budget accidentally.
6. Normal rejections use 429 and a useful `Retry-After`. Identity/counter outages
   have separate, documented behavior and observable errors.
7. Multiple gateways share enforcement or use a documented allocation scheme;
   merely putting an independent counter on each is insufficient.
8. Logs and traces can explain classification; credentials are never logged and
   tenant/user/key identifiers are not metric labels (ADR 33).

## Gateway evaluation

Evaluate native capabilities, not headline throughput. The required sequence is
early abuse protection → private identity lookup → tenant fairness → application.
Deployment portability, tenant overrides, multiple gateway instances, and
observable outage behavior are selection criteria. Avoid custom gateway plugins
or a new authentication system if a native external-auth interface suffices.

- [Envoy external authorization](https://www.envoyproxy.io/docs/envoy/latest/configuration/http/http_filters/ext_authz_filter.html)
  supports an application-owned HTTP identity check.
- [Envoy global rate limiting](https://www.envoyproxy.io/docs/envoy/latest/configuration/http/http_filters/rate_limit_filter)
  supports descriptors and policy overrides, using a separate limiter service.
- [Traefik rate limiting](https://doc.traefik.io/traefik/reference/routing-configuration/http/middlewares/ratelimit/)
  supports header-based partitions and native Redis counters across gateways.
  Validate tenant-specific policy selection after identity lookup before choosing
  it; a static middleware rate alone does not satisfy tenant overrides.

The reference selects **Envoy 1.39.1 + the standard Envoy rate-limit service +
ephemeral Redis**, pinned by image digest in
[Compose](../deploy/gateway/compose.yaml). No gateway plugin or custom counter
algorithm is introduced. Native external-auth classification followed by
descriptor-based policy handles tenant overrides. Redis preserves the allowance
when requests are unevenly distributed between gateways. Dividing a local
budget by gateway count would strand capacity when a client is pinned to one
gateway, so that simpler allocation is not the reference behavior.

Identity and business requests have separate upstream clusters, although both
discover the same API replicas. The first proof failed replica-distribution
validation with one shared cluster: sequential identity subrequests consumed
alternate round-robin turns and business requests repeatedly hit one replica.
Separate clusters passed the same fixture check.

The local authentication provider also used a per-process numeric sequence for
external organization IDs. A new replica or restart could reuse an existing ID
and fail onboarding with a uniqueness violation. Its organization and invitation
IDs now use UUIDv7. This is a local-reference fix, not a change to the production
WorkOS provider. The local provider's invitation list remains process-local and
ephemeral; use the WorkOS emulator/provider for directory lifecycle acceptance.

## Run the portable local reference

Prerequisites: Docker Compose, .NET 10 SDK, Node 24, Python 3. The existing .NET SDK
container publisher builds the application image; no new Dockerfile is needed.

```bash
tools/gateway-stack.sh up 2 2   # API replicas, gateway replicas
tools/gateway-stack.sh compose port --index 1 gateway 8080
# Use the printed address; Docker can assign a different host port on restart.
FLEET_BENCH_FIXTURE="$PWD/coverage/gateway/fixture.local.json" \
  node tools/fleet-bench.mjs http://127.0.0.1:PRINTED_PORT 15 32 2
node tools/gateway.test.mjs
tools/gateway-stack.sh down
```

The script seeds one API before scaling out, persists its local keyring/storage
under ignored `coverage/gateway/runtime`, and creates a private random gateway
key there. Only gateway ports are published. This is an **HTTP, Development-mode
reference**, with local auth and local adapters, not a production-ready provider
deployment. Production forks supply TLS, real adapters, protected keys, durable
business storage, and network policies. Never publish the generated runtime
directory: it contains the gateway key, data-protection keys and local fixtures.

Edit `coverage/gateway/runtime/ratelimit/config/policy.yaml` to exercise native
hot policy reload. The tracked [policy](../deploy/gateway/policy.yaml) is the
initial default; startup preserves local edits. Change the tracked
[Envoy configuration](../deploy/gateway/envoy.yaml) and rerun `up` to regenerate
bootstrap configuration and restart the gateway. This local script restarts and
temporarily reduces the fleet during setup; it is not a rolling-deploy tool.

## Current defaults and failure semantics

| Control | Reference behavior |
|---|---|
| Authenticated partition | Active organization across user, contact and service credentials; user ID only when no organization is active |
| Tenant policy | 200 requests per fixed UTC second and 6000 per fixed UTC minute; explicit per-tenant overrides in native limiter config |
| Anonymous policy | Trusted client IP, 20 per second and 600 per minute; a supplied guest cookie does not mint a new allowance |
| Early protection | Local gateway token buckets: 1000/s per IP and 2000/s per gateway, bounded descriptor cache; 10,000 downstream connections |
| Identity cache | 15 seconds, 10,000 entries per API by default; hash of cookie and authorization headers, never a raw credential cache key |
| Application admission | `Traffic:MaxConcurrentRequests=128` in the Docker reference (standalone default 32), shared by all non-probe requests in that process, including identity lookups; queue length zero |
| Fairness rejection | 429 with one `Retry-After` from the limiting window |
| Application overload | 503 with `Retry-After: 1`; liveness/readiness remain callable |
| Identity lookup failure | Gateway fails closed with 503 on transport/service failure; ordinary invalid credentials keep their authentication response |
| Counter-service failure | Gateway fails open after a 100ms timeout; local protection stays active; global tenant fairness is temporarily degraded |
| Limiter restart | Redis retains counters; policy updates at the same partition/window do not reset them |
| Redis restart | Ephemeral counts reset; at most a fresh configured window allowance is available after each reset; repeated resets repeatedly renew it |

These are starting settings, not measured production sizing. Fixed windows can
admit two adjacent windows' allowances close to a boundary. Redis uses bounded
memory with `noeviction`; exhaustion produces an observable dependency error
instead of silently renewing selected tenants' allowances. Monitor Envoy limiter
error/fail-open counters, overload rejections, downstream connections, API
in-flight work and Redis memory. Descriptor metrics in the limiter service are
disabled because exact tenant overrides include tenant IDs in metric names;
Envoy's aggregate metrics remain available through its private admin listener.

`Gateway:IdentityKey` enables the private lookup. `Gateway:Required=true` also
rejects direct non-probe requests without the gateway key. The reference enables
both. Outside the reference, the default standalone application supplies only
local concurrency protection; it makes no fleet-wide fairness promise. Removing
the old SQL limiter is therefore a deployment change, not just a binary upgrade.

For a replacement gateway, copy only credential headers to the private lookup,
inject its shared key and the trusted client IP, and use the returned
`X-LocIntel-Fairness-Partition` only for admission. Strip caller-supplied internal
and forwarded headers. The actual application request carries the original
credentials and gateway key; internal classification headers must not escape to
clients or confer application authority. Public/SSR requests must preserve the
actual tenant host through the trusted ingress, not accept an arbitrary
client-supplied `X-Forwarded-Host` as a tenant-selection override.

The application seam is the private HTTP classification contract; the selected
gateway's native routing and policy files are replaceable. `tools/gateway.test.mjs`
is the executable reference for expected behavior, but its Compose fault injection
and native policy mutations are Envoy-specific. A fork substitutes those setup
and fault operations while retaining the identity, shared-budget, spoofing,
revocation and failure assertions. It does not need an application-side gateway
interface or a new counter implementation.

## Migrate an existing fork

Before upgrading, run the read-only
[legacy policy inventory](../deploy/gateway/export-legacy-policy.sql) using an
identity that can read across forced RLS. It reports the previous effective
minute allowance, assigned source and active exception expiry for every org.
Review which values should become operational overrides; a former billing-plan
value is not automatically the right fairness setting. To retain a minute
allowance, set the tenant's sustained-window override to that value and choose a
separate burst. Expiring exceptions require a scheduled configuration change;
do not silently convert them into permanent overrides.

Deploy the gateway policies and verify identity/fairness before cutting traffic
over. Drain old API/worker binaries before applying `RetireRequestRateWindows`:
the migration drops an ephemeral table that old binaries still reference.
Deploy the new binaries and check that rate-window SQL is absent. The migration's
Down recreates the empty counter table for binary rollback; it does not restore
spent allowances. The migration does not delete entitlement assignments or
exceptions, but the retired code is absent from the catalog, plan bundles and
operator write surface. Existing billing synchronization removes obsolete
plan-owned rows on a later subscription update; operator rows are left alone.
Export and retain the inventory **before upgrading**, rather than relying on
those live tables as a permanent history. Future billing updates do not set
traffic policy. Normal offboarding/retention rules still apply.

## Verification ledger

| Stage | Required evidence | Current state |
|---|---|---|
| Policy | Accepted decision; existing settings migration specified | ADR 54 and read-only inventory/runbook implemented; inventory executed against the isolated reference database |
| Identity proof | Real cookies, API keys, contacts, switches, invalid credentials; bounded classification cache | Seven integration checks passed; includes one-slot DB pool held while a cached lookup succeeds and a revoked key denied at business endpoint |
| Reference deployment | Runnable gateway; private backend; replaceable documented contract | Two API + two gateway containers ran; gateway header contract, encoded private routes and business replica spread checked |
| Removal | No SQL rate-counter path or request-rate subscription entitlement; local overload protection | Source removed; additive migration applied; generated SQL reviewed; full platform Up/Down/Up integration test passed; 25 focused checks and updated container proof passed |
| Fairness | Noisy tenant throttled while quiet tenant retains latency/throughput; multiple credentials aggregate | Functional control passed: 20 shared admissions/20 rejections across two keys/two gateways; quiet tenant 10/10 accepted, max 20.1ms in the expanded Redis/scaling run. Sustained two-minute contention passed; see measured result below |
| Scaling | Aggregate allowance holds across application and gateway replica changes | Spent allowance remained rejected after scaling from two API/two gateways to three API/three gateways; sustained loaded scaling remains pending |
| Failure | Policy update, revocation, identity/counter outage, spoofing and direct bypass checks | Hot update, spent counter preservation, forged partition, encoded private routes, limiter stop/restart passed; Redis stop/restart also passed; isolated identity transport failure also passed (503 while the API remained alive, then enforcement recovered) |
| Capacity | Representative 1/2/4 API replica arrival-rate matrix with protections enabled | Small-data site-list matrix passes at 1000 arrivals/sec for 120 seconds after target-rate warmup, admission 128; six-route mix at 1,006 sites passes at 250/sec, fails at 500 and 1000/sec |
| Endurance and recovery | Sustained load, resource/backlog stability, replica loss and recovery | Ten-minute mixed 250/sec run passes with stable sampled API memory, balanced full minutes and healthy limiter; hours-long, background-write and loaded-failure qualification still pending independent hosts |

Previous measurements remain in [capacity validation](capacity-validation.md)
and the [assessment](performance-and-scalability-assessment.md). They describe
the SQL-counter implementation, not the replacement.

### Integration verification, 2026-09-07

Both current integration shards completed normally: 178 and 133 passed, with
one intentional opt-in scale skip. The first attempt at shard 1 passed all
assertions but hung during completion and was aborted after five minutes. Its
mini dump exposes thread stacks but not the heap required to identify an async
wait. A replay with full-dump capture armed exited normally; the historical
shutdown risk is not established as fixed. Use `HANG_DUMP_TYPE=full` with
`tools/run-integration-shard.sh` when investigating recurrence. Full dumps may
contain credentials and must remain private. An initial shard 2 wrapper failure
was caused by editing its script while it ran; the unchanged replay passed.

Gateway restart checks wait for each gateway to receive a limiter response,
not just for the limiter process health endpoint. Before its gRPC connection
recovers, the documented fail-open policy can admit traffic. Two initial local
control runs failed because they measured during this recovery interval; the
readiness precondition now makes the steady enforcement measurement explicit.
The failure controls separately verify that outage behavior.

The local authentication provider previously generated organization IDs from a
per-process sequence. Fresh replicas could collide on the database external-ID
constraint. It now generates UUIDv7-based organization and invitation IDs; the
seven-test identity suite includes organization creation through separate hosts.
This fixes ID uniqueness in the local development provider; it does not make its
process-local invitation dictionary a production shared service.

### Repeatable local capacity probe

With the reference already running, use `K6=/path/to/k6 tools/gateway-capacity.sh 1` (then 2 and 4). Set `RATE`, `CAPACITY_SECONDS`,
`VUS`, and `ROUTES` as required by the existing k6 harness. The probe scales only
API containers, prepares a real credential fixture, records the actual policy,
warms separately, and captures raw samples, SQL costs, resources and per-minute
results under ignored `coverage/gateway/capacity-*`. It enables the preloaded
`pg_stat_statements` extension in this isolated database. It does not change
traffic policy or relax acceptance thresholds.

Before measuring 1000 req/sec for one tenant, explicitly configure that tenant's
operational allowance above the offered workload. The default 6000/minute is
100/sec on average and should reject a sustained 1000/sec single-tenant test.
Retain the policy with results; an elevated test allowance is not evidence that
normal noisy-neighbor protection was exercised. The functional suite separately
uses small allowances to verify rejection, isolation and shared budgets.

The gateway control job is wired into the repository's required CI aggregate.
Its local controls passed; hosted execution has not been run from these
uncommitted changes. CI uploads only the result JSON, never the private runtime
or credential fixture. No production capacity threshold is inferred from CI.

### Initial replacement throughput result

At one API replica, two gateways and one worker, the first completed 60-second
1000-arrivals/sec site-list probe returned 59,864 valid successes and 137 HTTP
503s, with zero dropped arrivals. Successful throughput was 997.63/sec;
all-response p95/p99 were 13/23ms. This **fails** the required error and successful
count thresholds. Gateway aggregate counters recorded no tenant over-limit
responses or limiter fail-open errors over the capture interval. Both identity
and business upstreams returned 503s; the interval includes fixture setup, so
those upstream counters cannot be assigned one-for-one to measured requests.
Do not call this a qualified 1000 req/sec result.

Evidence: `coverage/gateway/capacity-1-20260907T142429Z`, including raw k6 samples,
manifest, actual operational policy, SQL statistics, resources and gateway
counter snapshots. The preceding attempts at `142018Z` and `142320Z` failed
before measurement (post-scale sign-in 503s and missing statistics-extension
initialization, respectively). They are setup findings, not throughput results.

The comparable two-API run (`capacity-2-20260907T142610Z`) returned 59,891 valid
successes and 109 HTTP 503s, zero dropped arrivals, 998.86 successes/sec, and
p95/p99 19/43ms. It also fails acceptance. All failures occurred in the first ten
seconds of measured traffic. Both runs used a separate 10-second warmup at only
50/sec; they therefore do not establish steady-state failure rates at a warmed
1000/sec load. A target-rate warmup comparison is required before choosing an
admission-limit change. The initial results remain evidence of startup exposure.

### Target-rate warmup comparison (local admission 32)

The next runs used a separate 30-second 1000/sec warmup and 120-second measured
window. All retained the same tenant allowance, queue-free admission limit of
32 per API, 20-slot primary database pool, and two gateways. All failed the
successful-count/error threshold despite zero dropped arrivals, zero rate-limit
errors/fail-open admissions, and replica-share checks passing.

| API replicas | Valid successes | HTTP 503 | Success/sec | p95 / p99 (ms) | Evidence suffix |
|---|---:|---:|---:|---:|---|
| 1 | 119,662 | 338 | 997.15 | 17 / 34 | `143715Z` |
| 2 | 119,759 | 241 | 997.94 | 26 / 54 | `143402Z` |
| 4 | 119,834 | 167 | 998.46 | 46 / 108 | `143037Z` |

The warmup hypothesis alone was insufficient: failures also occurred later in
the four-replica run. Exact gateway snapshots attribute its 167 rejections to
83 business-upstream 503s and 84 identity-upstream 503s. The relevant application
paths emit 503 through the local concurrency limiter; readiness is a separate
probe. The next controlled comparison changes only that operational concurrency
setting to 64. These results do not justify raising unbounded concurrency or
removing overload protection.

The one-replica 64-slot comparison (`capacity-1-20260907T144041Z`) still failed:
119,807 valid successes, 194 HTTP 503s, zero dropped arrivals, 998.31 successes/sec,
and p95/p99 15/32ms. A larger bound improved this sample but did not qualify it.
The next bounded experiment captured native admission and GC counters; the
reference default was subsequently set to the passing value of 128. `TRAFFIC_MAX_CONCURRENT_REQUESTS` configures
the reference deployment; it is independent of gateway rate policy.

One additional measured opportunity remains deliberately separate: API-key
requests still traverse guest-only middleware because those checks use the
cookie `ClaimsPrincipal`, whereas service identity is stored in request items.
The one-replica warm sample recorded one unsuccessful organization-by-host lookup
per successful service request (119,662 calls). A later change can skip guest
session creation and guest host resolution for service principals, with explicit
regressions for guest, cookie and API-key behavior. This is an observed redundant
query, not a claim that removing it alone proves production capacity.

### First passing bounded-admission runs (128 slots)

At 128 local slots, the one- and two-replica runs each passed the strict
120-second, 1000-arrivals/sec gate with 120,000 valid successes, zero failures,
zero dropped arrivals, and passing gateway-enforcement and replica-share checks.
One replica recorded p95/p99 12/24ms (`144711Z`); two recorded 19/41ms (`145117Z`).
Four replicas also passed with p95/p99 36/85ms (`145509Z`). These are short, warm, single-host
site-list results against the small seed dataset, not endurance, mixed-workload,
independent-host or production-capacity certification.

The one-replica diagnostic capture confirms zero native global-limiter
rejections during the measured interval. It recorded 136 during the preceding
startup/warmup interval, so readiness does not imply fully warmed throughput.
During the measured 120 seconds, the runtime allocated about 44.1GB cumulatively
(roughly 368KB per business success, including identity lookups and instrumentation)
and reported 2.25 seconds of aggregate GC pause time. These are allocated bytes,
not retained memory or a leak diagnosis. The final recorded post-collection heap
was about 51MB while process working set peaked around 1.17GB over the full
capture. Allocation profiling and longer memory-stability tests remain useful.

The reference has no individual CPU or memory limits; each process shares a
10-vCPU, 7.75GiB Docker VM in these runs. Production forks must budget API,
worker, database, gateway and limiter resources separately. Adding processes
on this host adds runtime and GC overhead without adding physical resources;
the short replica matrix does not measure scale-out on additional compute.

The current direct-database fleet suite passes all four scenarios: shared session
handling, cross-replica idempotency, worker sweep leases, and completion of all
400 imported sites after killing the API that accepted the commit. The whole scenario took
about 79 seconds, including upload/preparation; this is durable-work recovery evidence, not a
claim of uninterrupted request availability. Its harness removes the database
while terminating processes, so teardown connection errors are not a graceful
production-drain test. The transaction-mode PgBouncer replay also passed all four scenarios; its
whole kill-and-recovery case took about 70 seconds, including preparation.
Public frontend tests pass 11/11 and workspace typechecks pass after the SSR host
change. The final eight-group gateway run also passed, including the real public-host regression.

### Public SSR integration

Node's built-in `fetch` discarded a supplied `Host` header in a real local
listener check. The first public gateway regression therefore received an empty
page instead of the tenant's locations. Public reads, contact redemption and
logout now share a small Node HTTP/HTTPS transport that preserves tenant Host,
forwards only supplied cookies, keeps redirects manual, preserves separate
Set-Cookie headers and propagates the existing abort deadline through body reads.
It requests identity encoding and bounds the stream adapter's byte queue.
No new dependency or gateway plugin was added. The real public-host gateway
check passes; public tests now pass 14/14, including TCP transport and body-abort
regressions, and the public build/typechecks pass.

The reference gateway sees the network peer's IP. Anonymous SSR calls therefore
share the SSR server's egress-IP allowance, as clients behind a NAT do. This is
not per-browser abuse protection behind a frontend server. Forks must enforce
end-client IP protection at their trusted outer ingress and size the internal
SSR allowance explicitly; never fix this by trusting arbitrary incoming
X-Forwarded-For headers. The portable reference demonstrates the API gateway,
not a complete frontend/CDN/TLS production deployment.

Gateway controls use Node 24 to exercise the same TypeScript server transport
directly without another test runner or compile step. Product frontend builds
continue through the existing Vite pipeline.

A repeated control run failed when its scaling stage followed a Redis restart:
one decision admitted a previously spent tenant. Limiter logs also recorded a
Redis DNS/connection error during that restart. The cause of that individual
decision was not conclusively captured. Fault recovery is now tested last, with
20 concurrent rejection decisions required before declaring recovery; scaling
has its own clean precondition and records decision status/Retry-After. The
subsequent eight-group run passed. Neither a single health response nor one
correct decision should be treated as proof that every pooled dependency
connection has recovered.

### Sustained tenant contention

The two-API/two-gateway, 128-slot reference passed a two-minute run with 1000
noisy-tenant arrivals/sec (two credentials) plus 10 quiet-tenant arrivals/sec.
The noisy policy was 20 attempts/second and 1200 attempts/minute; the quiet
policy was 100/second and 6000/minute. Results: 1,201/1,201 quiet successes,
p95 9.1ms and maximum 73.2ms; 120 noisy successes and 119,881 expected 429s;
zero unexpected responses, zero dropped arrivals, and zero gateway limiter
errors/fail-open admissions. Evidence: `coverage/gateway/noisy-neighbor-20260907T152341Z`.

Counters represent attempts, not purchased successful-request credits. A burst
rejection can still consume the other window's counter; sustained spam exhausts
the minute budget quickly. That explains the noisy tenant's low successful
average despite a nominal 20/sec burst setting. This is intentional operational
protection, not a guaranteed admitted throughput. The executable workload is
`tools/gateway-noisy-neighbor.k6.js`; prepare its private `FAIRNESS_FIXTURE` with
`tools/gateway.test.mjs`, configure/retain the native policy, and pass gateway
`BASE_URLS`, `SUMMARY`, and optional `CAPACITY_SECONDS` to k6. Never publish the
credential fixture. The preceding `152117Z` attempt failed before measurement
because its readiness probe was slower than the limit it was trying to exceed.

### Larger mixed workload: 1000/sec failed

The two-API reference was seeded through the real API to 1,006 sites, then tested
with equal arrival shares for site list, search, detail, 500-row feed, public list
and nearest-site reads. At 1000 arrivals/sec for 120 seconds after a 30-second
target-rate warmup, it returned 74,086 valid successes and 9,515 HTTP 503s, with
36,400 dropped arrivals. Valid throughput was 616.03/sec; all-response p95/p99
were 627/795ms. Every HTTP 200 passed its shape/nonempty check. Gateway counters
recorded 19 limiter errors/fail-open admissions, so enforcement qualification
also failed. Evidence: `capacity-2-20260907T153625Z`.

The small-data site-list result must not be generalized to this workload.
The mixed sample returned millions of database rows; its search-count query
alone accumulated 58.5 seconds of execution across 12,329 calls. Sampled median
CPU was about 223% and 233% for the APIs, 181% for PostgreSQL, plus gateway,
limiter and native-generator work on the same physical host. These observations
do not isolate a single production bottleneck. The next test lowers the offered
rate before an endurance run; no timeout, error threshold or limiter timeout
was relaxed to obtain a pass.

Mixed public traffic requires an explicit benchmark-source allowance: normal
anonymous 20/sec protection would intentionally reject it. Automatic approval
review rejected a proposed persistent wildcard guest-limit increase. It was not
applied. The accepted alternative temporarily overrides only the observed local
benchmark IP `172.18.0.1`, retains the default guest rules, records the policy,
and restores it on exit/interruption. Restoration was verified after this run.

The matching 500/sec mixed run (`154256Z`) improved throughput delivery but still
failed latency: 59,993 valid successes, 7 HTTP 503s, zero dropped arrivals, and
p95/p99 233/384ms over 120 seconds. All six per-route latency gates failed.
Lowering offered load is being used to find a latency-compliant baseline; the
error/latency thresholds remain unchanged. A low error rate alone is not an
acceptable capacity result.

The built public server was also smoke-tested through the gateway with the
actual tenant Host: server-rendered HTML contained the expected Ballard site.
The temporary SSR smoke process was stopped afterward.

The corresponding 250/sec mixed run (`capacity-2-20260907T154940Z`) passed:
30,001 valid successes, zero HTTP errors or dropped arrivals, p95/p99 33/95ms,
all six route gates and replica-share checks passing. Gateway counters recorded
30,001 admissions with no errors, fail-open or over-limit decisions. This is a
two-minute baseline at 1,006 sites, not a measured exact maximum.

### Ten-minute mixed endurance result

`coverage/gateway/capacity-2-20260907T160108Z` passed at 250 offered arrivals/sec
for 600 seconds after 30 seconds of target-rate warmup: 150,001 valid successes,
zero HTTP errors, zero dropped arrivals, all-response p95/p99 15/21ms and maximum
318ms. Every route passed its aggregate gates. Every full completion-minute bin
contained 14,999–15,001 successes; each API served 7,499–7,501 of them. The worst
per-route full-minute p95/p99 were 19/30ms. Partial edge minutes also passed the
latency budgets. Gateway counters recorded zero limiter errors, fail-open or
over-limit decisions.

Across 121 resource snapshots, API memory stayed in ranges 1.276–1.365 GiB and
1.065–1.109 GiB; median API CPU was 69.45% and 67.87%, PostgreSQL 43.97%.
Sampled dead-letter count stayed zero. Raw resources, per-minute results and
`resource-summary.json` are retained with the run. These are container memory
samples, not retained-heap measurements or a proof against leaks. The worker was
running, but this was a read workload without a concurrent import/write campaign.
Lower tails than the earlier two-minute sample are another reason to repeat
external comparisons rather than treating one local run as a universal limit.

One authenticated private-route probe during this measurement returned 200 for
`/_GATEWAY/identity`: ASP.NET path matching accepted casing that the Envoy deny
route did not. Internal response headers remained stripped. That extra probe is
outside k6's business count and accounts for the one extra gateway admission.
The private-prefix match is now explicitly case-insensitive; uppercase and mixed
case regression requests were added. All eight gateway control groups passed
again after applying that configuration. Throughput measurements above used the
previous bootstrap hash; the subsequent change only tightens private-path denial.

The temporary benchmark IP allowance was restored on exit. After final controls,
the runtime policy was verified byte-for-byte against the tracked default, with
no elevated benchmark tenant/IP rules left active. Historical SQL-counter result
files were copied from temporary directories to ignored
`coverage/performance/historical-sql-counter`; its index records original paths
and file hashes. Credentials, runtime keys, application logs and dumps were
excluded from that archive. Current gateway evidence remains in ignored
`coverage/gateway`; never publish that whole directory because it also contains
private fixtures and runtime material.
