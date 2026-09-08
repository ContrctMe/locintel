# Deploying a fork to production

The AppHost is **dev-only orchestration**. In production you run one
container image (`src/LocIntel.Api`) in three roles, two static frontend
builds, Postgres, and your chosen adapters. This page is the complete story:
topology, the database role split, every configuration key, the boot guards,
and how the system degrades when a dependency is down.

## Topology

One image, three roles, selected by the `ROLE` environment variable (ADR 34):

| Role | What it does | Runs |
|---|---|---|
| `migrate` | Applies all configured modules' EF migrations with **owner** credentials, provisions the unprivileged `app_user` role, reassigns schema ownership, exits | Once per deploy, before the others |
| `api` | HTTP surface (Wolverine endpoints, auth, webhooks) | 1+ replicas — sessions, idempotency and business capacity reservations live in Postgres; gateway request fairness is operational policy (ADR 54). Local concurrency, the brief fairness-identity cache and cluster-tile caches are per replica by design |
| `worker` | Outbox delivery, scheduled retries, occurrence materialization, retention purge, idempotency cleanup | 1+ replicas — every recurring sweep is leased per period in `platform.sweep_runs` (first replica to claim `(sweep, period)` runs it, the rest skip), so replicas never duplicate a sweep |

A connection pooler in front of Postgres runs in **transaction mode**
(ADR 53): the tenant variable is transaction state (`SET LOCAL`, set when
a transaction starts and carried in the batch of any read outside one), so
a server connection that moves between clients between transactions
carries nothing from the last one. Two settings make it work: point
`ConnectionStrings:locintel-messaging` at Postgres directly, because
Wolverine's node agents and leader election hold session-level advisory
locks that a transaction pool cannot carry (the app-role rewrite applies to
it too), and give the pooler `max_prepared_statements` (PgBouncer 1.21+).
The api and worker connect through the pooler; the migrate role connects
directly. `docs/scaling.md` has the measurements; `LOCINTEL_PGBOUNCER=1
aspire run` and `tools/replica-stack.sh N --pgbouncer` run the same
container locally. Session mode still works and is no longer the
recommendation.

Two and four replicas of each role are proven by the fleet suite:
`tools/replica-stack.sh 2` boots the topology above on one host (a
round-robin proxy in front) and runs `tests/LocIntel.FleetTests` - one
session answered by every replica, an idempotency key honoured across
replicas, one sweep claim
per period across workers, and a replica killed while its local queue
still holds a committed batch, whose messages the survivors finish.
`tools/replica-stack.sh 4 --bench` runs the load baseline through the proxy;
`docs/scaling.md` holds the table. Every piece of per-process state is
either shared through Postgres or listed in the table above as per replica
on purpose; add a new one to one of those two places. Request fairness now has
its own [gateway reference and acceptance suite](gateway-fairness.md), including
two gateway replicas sharing one tenant allowance. The standalone fleet proxy
does not implement gateway fairness.

Deploy traffic admission before cutting over from the SQL limiter. The native
gateway reference uses Envoy plus its standard limiter service and ephemeral
Redis; forks may substitute a gateway that passes the documented identity,
fairness and failure contract. API/identity listeners stay private, clients
cannot supply trusted classification headers, and the application still validates
credentials and gates each business request. The provided Compose file uses local
development adapters and HTTP; supply production TLS, adapters and protected
secrets before deploying it. Read the migration section before dropping the
retired counter table, since old binaries still reference it.

The local gateway Compose network defaults to `172.31.249.0/24`. Set
`GATEWAY_SUBNET` to a nonconflicting private CIDR when necessary; the same value
configures Docker's network and the application's trusted immediate proxies.
Every container joined to that network belongs to the local proxy trust boundary.

Ordering matters: `api`/`worker` should start (or restart) after `migrate`
exits successfully — the Aspire graph does this with `WaitForCompletion`;
in Kubernetes use an init container or a migration Job; in Compose use
`depends_on: condition: service_completed_successfully`.

The API and worker serve two probes. `GET /livez` returns 200 as soon as the process
serves requests — wire it to your liveness probe (restart on failure).
`GET /healthz` returns 200 when the checks below pass (503 otherwise),
and reports its role and version — wire it to your readiness probe
(out of rotation while failing). An unknown `ROLE` refuses to start.

Readiness requires completed bootstrap, a started/non-cancelling Wolverine
runtime, accepting/non-faulted listeners, and an available durable local queue
with no latched local queues. Under the actual application database identity it
checks schema usage plus each SELECT/INSERT/UPDATE/DELETE privilege on the
incoming, outgoing, and dead-letter tables, and on `identity.user_sessions`
for the API or `platform.sweep_runs` for the worker. Both roles handle durable
local messages, so both require the envelope permissions. Database checking is
bounded to three seconds. Runtime state uses the public Wolverine runtime,
sending-agent, and listener-circuit APIs, without probing private fields.

This is a point-in-time readiness contract, not a complete database-permission
audit or a promise that every handler will succeed. It does not execute a
business transaction or verify external providers. Monitor backlog age, completed
work, retry/dead-letter rates, and sweep completion separately: a running queue
can still be failing a specific message. Provider diagnostics stay on the
operator surface; vendor outages do not turn process-local liveness red.

Sweep keys use the message contract's assembly and fully qualified type name, so
same-named contracts cannot suppress each other. The lease is committed before
the durable messages are published: scheduling is at most once per period, while
successfully published messages use Wolverine's durable delivery. A crash in that
small gap, or partway through per-org fan-out, delays the missing work until the
next period. Every shipped sweep is condition-based and self-repairing, so this is
the intended guarantee; make the claim and outbox publication transactional before
adding a sweep whose individual period must never be missed.

### Audit partition maintenance

The worker publishes one daily `MaintainAuditPartitions` operation through the
global sweep lease. Per-tenant `PurgeAuditData` only deletes rows past that
tenant's entitlement window; it never creates or drops partitions. Global
upkeep ensures current/next month and prunes **empty** monthly partitions older
than 400 days. The threshold controls empty-table housekeeping, not data
retention: populated partitions survive until tenant retention empties them,
including the Scale plan's 730-day window and longer overrides.

The additive `SafeAuditPartitionMaintenance` migration replaces the existing
SECURITY DEFINER functions without changing applied migrations. A shared
transaction advisory lock serializes concurrent calls and redelivery. Missing
months are rebuilt by moving matching default-partition rows and attaching the
new partition in one transaction; FORCE RLS and the tenant policy are installed
before commit. Failure rolls back both movement and partition DDL. Wolverine
handles retries after publication; claim-to-publication failure retains the
next-period recovery limit described above.

The function owner needs visibility across forced RLS for these operations.
`row_security=off` makes insufficient privileges fail rather than mistaking a
filtered partition for an empty one. The app role still receives no DDL rights.
Missing-month repair and old-partition pruning take an exclusive parent-table
lock, so large backfills can temporarily block access-log writes. Monitor failed
upkeep, default-partition growth, and lock duration; move to staged/online repair
only when measurements justify it. The default partition is never dropped.

## The database role split (ADR 38 — do not skip)

Two Postgres identities:

- The **owner** (whatever your platform provisions) is handed **only to the
  migrate role** via `ConnectionStrings:locintel`.
- `api` and `worker` receive connection strings containing **only application-role
  credentials**, plus matching `Database:AppUser` / `Database:AppPassword` when
  used. Store those separately from the migration/admin Secret, including the
  optional messaging connection. A configuration rewrite cannot erase owner
  credentials from a process environment or mounted file; never pass them to a
  serving role. AppHost and the DigitalOcean manifests use this same separation.

This is what makes row-level security real: `app_user` is subject to RLS,
the owner is not. **If api or worker ever connects as the owner, RLS is
silently inert** — every tenant can read every row and nothing errors. The
migrate role provisions `app_user` itself (password comes from
`set_config`, parameterized — check `MigrationRunner.cs` before changing it).

Applied migrations are immutable — new migration, never an edit (a repo hook
enforces this; your CI should too via the round-trip tests).

## Configuration reference

### Handler and transaction conventions

- A query endpoint with an explicit Wolverine transaction uses
  `[Transactional(typeof(OwnerDbContext), Mode = TransactionMiddlewareMode.Lightweight)]`.
  This avoids holding an eager transaction/connection across calls to other
  modules. `TransactionalAttributeTests` checks every annotated Wolverine GET.
  Lightweight is not a read-only guarantee; review side effects separately.
- A write endpoint or durable handler names its owning DbContext and uses the
  normal eager transaction when read/lock/write must be atomic. Its state changes
  and outgoing durable messages belong to that same transaction. Calling another
  module's interface does not enlist that module's DbContext automatically.
- Acquire admission locks before reading mutable quantities. Use the existing
  platform capacity helper for accepted site imports; never add a separate
  process-local business counter. See [ADR 55](decisions/0055-business-capacity-reservations.md).
- Keep organization identity explicit: HTTP work obtains it from the validated
  principal; queued work obtains it from the message envelope. Queries still
  apply scope, and database operations still use the unprivileged runtime role.
- Build from current generated handlers using the sequence below. After changing
  handler signatures, clear stale disposable `Internal/Generated` sources before
  rebuilding/regenerating; never treat them as hand-maintained application code.

The migration role retries transient Npgsql failures such as startup
unavailability. Syntax, credential and privilege failures stop immediately.
The deployment operator fixes the cause before intentionally rerunning migration.

Everything the image reads. Section syntax (`A:B`) maps to env vars as
`A__B` (double underscore).

### Core

| Key | Required | Notes |
|---|---|---|
| `ROLE` | yes | `migrate` \| `api` \| `worker` (default `api`) |
| `Build:Version` | recommended | Stamp it in CI (e.g. the git SHA or tag); surfaces in `/healthz` and the console footer so "what version are you running?" is answerable |
| `ConnectionStrings:locintel` | yes | Application-role credentials for api/worker; owner credentials only for migrate. Omitted `Maximum Pool Size` defaults to 20 per pool (or an explicitly larger `Minimum Pool Size`); explicit maximums are preserved |
| `Database:AppUser` / `Database:AppPassword` | api/worker | The RLS-subject identity |
| `Public:HostTemplate` | yes | e.g. `https://{slug}.yourproduct.com` — contact links are minted from this |
| `AllowedHosts` | outside Development/Testing, or when trusting a proxy | Semicolon-separated application hosts, e.g. `console.example.com;*.example.com`. Bare `*` is refused. Include the explicit internal probe host used by your orchestrator; the DigitalOcean renderer adds `health.locintel.internal`. |
| `Proxy:KnownProxies:{n}` / `Proxy:KnownNetworks:{n}` | when trusting a proxy | The actual immediate proxy IPs or bounded CIDRs. Never use `0.0.0.0/0` or `::/0`; a whole private network should be trusted only if its workloads are in the same trust boundary. |
| `Proxy:TrustForwardedHeaders` | behind a proxy | Accepts forwarded scheme/host/IP only from explicitly configured `Proxy:KnownProxies` IPs or `Proxy:KnownNetworks` CIDRs. Trusts one immediate hop; the proxy must strip client-supplied forwarding headers. A missing peer list or unrestricted CIDR refuses startup. |

Pool limits apply **per pool, per process**, not per database or deployment.
EF's regional data source, durable messaging, and direct Npgsql connections can
have separate pools. Budget their aggregate across API/worker replicas below
PostgreSQL's usable connection slots, leaving room for migration, diagnostics,
and shutdown work. A default of 20 is not a multi-replica capacity guarantee;
set native `Maximum Pool Size` explicitly when sizing a deployment. Measure
pool waits as well as server occupancy before increasing it.

### Auth (ADR 14)

| Key | Notes |
|---|---|
| `Auth:Provider` | `workos` in production (`local` refuses to boot there) |
| `Auth:WorkOS:ApiKey`, `Auth:WorkOS:ClientId` | From the WorkOS dashboard |
| `Auth:WorkOS:ApiBaseUrl` | Leave unset for real WorkOS (emulator is dev-only) |
| `Auth:WorkOS:WebhookSecret` | Required outside Development/Testing for signed security and directory events; invalid or unsigned deliveries are rejected |

Register the WorkOS webhook endpoint at
`https://api.yourproduct.com/auth/directory/webhook` for `session.revoked`,
`password_reset.succeeded`, and `user.deleted`. These events revoke that
provider user's local sessions created before the event. Add `dsync.user.created`,
`dsync.user.updated`, and `dsync.user.deleted` when using directory sync.
Register the AuthKit redirect URI at
`https://console.yourproduct.com/auth/callback` and test signed revocation delivery.

The console observes `X-LocIntel-Session-Context` on `/me` and sends that
fingerprint as a request precondition. Preserve this header through proxies.
It is not an authentication token: the encrypted HttpOnly cookie and normal
authorization remain authoritative. A mismatched fingerprint returns 409 before
business processing, preventing a stale tab from acting under another tab's
new cookie. Clients without the optional precondition retain their existing
API contract; deploy the matching console/API together. The console refuses a
successful `/me` response without this header and offers session verification
retry, rather than silently proceeding without stale-write protection.

Same-origin console tabs notify each other with BroadcastChannel after session
changes or fresh login. Focus/visibility checks detect changes from other paths;
stale requests also trigger a reset. A frozen tab is not claimed to update
immediately. Console API requests have a 30-second deadline covering the response
body; query cancellation aborts their network reads. Upload workflows have a
120-second overall deadline, including direct storage and scan polling. Polling
uses the tenant-authorized `/api/files/{id}` metadata endpoint, so concurrent
uploads cannot push the target off a list page. Public SSR upstream reads and
session actions also have a 30-second request deadline. These
are client limits, not a claim that server-side work rolls back on disconnect.
Interrupted writes are never automatically retried: the console warns that the
operation may have completed and asks users to refresh before retrying. Session
changes drain those bounded mutations and preserve the warning after discarding
the old tree. Larger/slower uploads require a deliberate deadline change and
slow-link acceptance tests. Remaining verification and failure cases are tracked
in the [current review](software-maturity-review-details.md).

### Billing (ADR 39)

| Key | Notes |
|---|---|
| `Billing:Provider` | `stripe` in production (`local` refuses to boot there) |
| `Billing:Stripe:ApiKey`, `Billing:Stripe:WebhookSecret` | Webhook endpoint: `/billing/webhook` |
| `Billing:Stripe:PriceIds:<planId>` | One Stripe price id per `PlanCatalog` plan |

### Email (ADR 32)

| Key | Notes |
|---|---|
| `Notifications:Transport` | `smtp` in production (`local` refuses to boot there) |
| `Notifications:Smtp:Host`, `:Port` | Submission endpoint (587 default, STARTTLS on) |
| `Notifications:Smtp:UserName`, `:Password` | Omit both for an unauthenticated relay |
| `Notifications:Smtp:FromAddress`, `:FromName` | The sender identity |

Email is on the **authentication critical path** (magic links deliver the
contact tier). Before go-live: publish SPF for your sending domain, sign with
DKIM (your provider's CNAMEs), and set a DMARC policy — without these,
contact links land in spam and that reads as "login is broken."

### Storage, secrets, misc

| Key | Notes |
|---|---|
| `Storage:Provider` | `s3` (`Storage:S3:BucketName`, optional `ServiceUrl`/`AccessKey`/`SecretKey`/`ForcePathStyle` for MinIO/R2) or `azure` (`Storage:Azure:ConnectionString`, `ContainerName`); both smoke-tested against MinIO/Azurite. `local` (`Storage:LocalRoot`) is **dev/test only — refuses to boot in Production**: tickets are signed with the shared local key and bytes live on local disk |
| `Scanner:Provider` | `clamav` (`Scanner:ClamAv:Host`, `Port` default 3310, `TimeoutSeconds` default 60; clamd with TCPSocket enabled) or a fork adapter behind `IVirusScanner`. `eicar` is **dev/test only — refuses to boot in Production**: it reads 128 KiB and knows one signature. A scanner that cannot answer keeps the object quarantined; it never reads as clean |
| `Secrets:Provider` | `kms` (`Secrets:Kms:KeyId`, optional `ServiceUrl`/`AccessKey`/`SecretKey`; ADR 31, LocalStack-tested) or a fork adapter. `local` (`Secrets:LocalMasterKey`, the default when that key is set) is **dev/test only — refuses to boot in Production** |
| `Traffic:MaxConcurrentRequests` | Per-process admission, standalone default 32; Docker gateway reference 128, configurable with `TRAFFIC_MAX_CONCURRENT_REQUESTS`. Valid 1–4096; no queue; overload returns 503 with `Retry-After: 1`. Includes private identity checks; excludes probes. Size against measured latency and resource budgets |
| `Gateway:IdentityKey` | Enables the private fairness-identity lookup; at least 32 bytes. Reference deployment generates a random key shared with the gateway; protect it like other service credentials |
| `Gateway:Required` | Default false for standalone use; the reference sets true and rejects non-probe requests without the gateway key. Network isolation is still required |
| `Gateway:IdentityCacheSeconds` / `Gateway:IdentityCacheEntries` | Defaults 15 seconds / 10,000 entries per API; bounded fairness classification only, never cached authorization |
| `Impersonation:TtlSeconds` | Support-session length (default 3600) |
| `Notifications:Sms` | `off` (default, and the only value allowed in Production without a fork adapter) or `local` (dev catcher). SMS is a SEAM: the template ships the port and an off transport, never a routing or consent policy |
| `Api:ExposeOpenApi` | Serve `/openapi/v1.json` (default true; the console developer page links it). Set false to hide the API surface |
| `Webhooks:RetryBaseSeconds` | Outbound webhook backoff base (default suits production; tests shrink it) |
| `Audit:PolicyCacheTtlSeconds` | Per-org audit-policy cache |

### Protect shared authentication keys

`DataProtection:CertificatePath` is required outside Development/Testing (except
for the migration role). It loads a PFX certificate with its private key and
encrypts persisted Data Protection XML using the framework's certificate
protector. `DataProtection:CertificatePassword` supplies its password. Mount the
same protected certificate and shared `DataProtection:KeyPath` for every replica.
The DigitalOcean compatibility manifests require this configuration. Linux uses
an ephemeral imported private key; macOS uses its supported default key storage.
A local integration test verifies encrypted XML, fresh-host decryption and failure
without the certificate. This is separate from business-secret KMS wrapping.
Certificate rotation retaining old decryptors still needs implementation before
rotating an existing deployment; never discard a certificate needed by live keys.

### Boot guards

Only the explicit `Development` and `Testing` environments permit local auth,
storage, key wrapping, billing, mail/SMS catchers and the EICAR-only scanner.
Staging and custom environment names use the same secure provider policy as
Production. WorkOS, Stripe, S3 and KMS endpoint overrides must use HTTPS; Azure's
parsed blob endpoint must use HTTPS and development-storage connection strings
are rejected. SMTP requires STARTTLS. Leave provider endpoint overrides unset
when using the vendor's normal HTTPS endpoint.

Scanner evidence: `ClamAvScannerTests` runs the production adapter against a real
`clamav/clamav:1.5.4-debian` daemon with bundled signatures. It covers clean and
infected uploads through durable processing, plus a paused-daemon timeout that
keeps the file unavailable until an explicit successful handler retry. This is
not evidence for signature-update operations, automatic retry timing, or cloud
storage: the pipeline fixture uses local disk storage. Cloud storage adapters
have separate MinIO/Azurite tests; KMS uses LocalStack, authentication uses the
WorkOS emulator, billing uses stripe-mock, and SMTP uses Mailpit. Live cloud IAM,
vendor account configuration, actual billing lifecycle, and email delivery remain
deployment-specific validation, not outcomes established by these local tests.

Upload safety: S3 tickets sign `If-None-Match: *` and the declared Content-Length,
so the provider enforces create-only writes of that size. Azure SAS cannot enforce
a byte ceiling; Azure uploads therefore use an authenticated, creator-bound API
relay with a 15-minute expiry. The relay admits one active upload per process,
uses a two-minute deadline, stages and checks actual bytes before writing the
cloud object, and refuses replay. Fork storage adapters must declare
whether their direct tickets enforce the same byte and create-only guarantees.
`IObjectStore.GetLengthAsync` returns actual stored length, null for absence, and
propagates other provider failures. Completion rejects empty or oversized objects
before publishing a scan. Each declared upload remains capped at 100 MiB.

An organization may have at most 20 unfinished/quarantined uploads with at most
500 MiB in total declared size. An hourly sweep erases unfinished/quarantined
objects older than an hour after locking and rechecking their state; legal holds
are preserved. Deleting an unfinished S3 upload keeps it quarantined, hidden and
charged against this budget until its signed ticket expires; existing bytes stay
in place so the create-only ticket cannot recreate them. These controls do not reconcile a provider write interrupted before
its database transaction commits. Configure provider lifecycle/reconciliation and
monitor incomplete uploads and storage spend. The Azure relay uses temporary disk;
size gateway limits and pod scratch capacity for the upload relay and reports.

Organization and audit exports share one queued/running admission slot per tenant.
The export worker processes one message at a time per process; each materialized
query is limited to 100,000 rows and 4 MiB of serialized data, and an organization
archive is limited to 32 MiB uncompressed. Organization exports exceeding a limit
fail without publishing an archive; audit exports identify truncation in their
manifest. Purged organizations cannot publish delayed exports. Raise these
budgets only with measured memory, scratch-space and storage capacity.

Configure S3 CORS for the actual console origins, PUT/GET, Content-Type and
`If-None-Match`. Preserve the browser's signed Content-Length; intermediaries must
not rewrite upload bodies. Azure uploads go through the same-origin API and do not
require browser SAS upload headers. Download origins still need their applicable
CORS policy. Previously issued unbounded/overwrite-capable URLs are not
retroactively revoked: stop issuing old tickets and allow their 15-minute TTL to
expire before trusting the new invariant, or revoke their signing credentials.
Review previously uploaded content if such URLs were exposed. Validate these
controls against the selected live provider; local evidence uses MinIO and Azurite,
not AWS/Azure/R2 accounts.

The image **refuses to start in Production** with any dev-only adapter
still selected: local auth, local storage, the EICAR scanner, the local key
wrapper, local billing, or local notifications - and with an unknown
provider name in any environment. `ProductionBootGuardTests` proves each
seam. Treat a failed boot here as the guard working, not a bug.

## Frontends and DNS

The two frontends deploy differently (`pnpm build` in `web/`):

- **Console** (`web/apps/console/dist`) is a **static SPA** — a folder of
  assets served by any static host or your reverse proxy.
- **Public app** (`web/apps/public/dist`) is **server-rendered** (TanStack
  Start). Its build emits a **server bundle** (`dist/server/server.js`, a
  web-standard `fetch` handler) plus client assets — it is NOT a static
  drop. It must run as a process on a Node or serverless/edge host, with
  `LOCINTEL_API` pointing at the API's internal URL (SSR fetches run
  server-to-server) and reachable from the org subdomains. `/public/*` API
  responses and the sitemap use `private, no-store`; rendered HTML is also
  `no-store`, since it contains the visitor's identity and a CSP nonce. Do not
  override these headers with shared CDN caching. Static assets and `robots.txt`
  may retain their explicit public cache policy. Shared caching of tenant data
  requires a separately designed public-only authority and cache key.

The session cookie is HttpOnly on a shared origin (ADR 21), so the console
and API must share a host through your reverse proxy. Route these path
prefixes to the API and everything else to the console bundle:

```
/api  /auth  /me(exact)  /objects  /openapi  /contact-links  /contact  /billing  /healthz
```

(That list is mirrored by `web/apps/console/vite.config.ts` — keep them in
sync. `/me` is an exact match: a prefix match swallows `/members`.)

Header custody: the API stamps `nosniff`, `X-Frame-Options`,
`Referrer-Policy`, and a deny-all CSP on its own responses, and 429s carry
`Retry-After`. **HSTS and the frontends' CSPs are the reverse proxy's job** —
TLS terminates there, and the static bundles need a script/style policy only
their host can own.

DNS: `console.yourproduct.com` (console + API) and a wildcard
`*.yourproduct.com` (public app; the org slug is the subdomain — this is
what `Public:HostTemplate` must match). Production subdomains do not share
cookies; only localhost's port-blind cookies do (the dev quirk in CLAUDE.md).

## Building the image

There is no Dockerfile to drift. The SDK builds the OCI image:

```bash
dotnet run --project src/LocIntel.Api -c Release -- codegen write   # pre-generate Wolverine handler code
dotnet publish src/LocIntel.Api -c Release -p:PublishProfile=DefaultContainer -p:ContainerImageTag=<tag> -p:Version=<version>
```

The image runs as the base image's non-root `app` user, listens on 8080,
and is the one artifact all three roles run from. CI (`checks.yml`, the
`image` job) builds it on every push and smokes it: `tools/smoke-image.sh`
boots `migrate`, `api` and `worker` from the image in **Production** mode
against a real PostgreSQL and asserts the migrate role exits cleanly and
both long-running roles answer `/livez` and `/healthz`. It starts the worker
before the API and requires the existing durable cleanup job to remove an
expired idempotency record while retaining a fresh one. It also rejects dead
letters, unexpected serving-role exits, error/exception logs, and root runtime
users. The expanded script's current verification status is tracked in the
[maturity review](software-maturity-review-details.md). That run is also the
boot guards' negative control - a production-valid configuration must not
be refused. `-p:Version` becomes `Build:Version`'s fallback in `/healthz`.

### Artifact and local runtime servicing

CI scans the immutable local image ID with a checksum-pinned Trivy release, produces a
CycloneDX SBOM, and fails on HIGH/CRITICAL OS or library advisories. The
`image-security` artifact includes scanner/runtime versions, local image ID,
registry digests (empty before pushing), SBOM and vulnerability results. The
scan reads Docker locally and downloads advisory databases; it does not submit
a dependency snapshot or image to an external scanner. Run the same check with
`tools/scan-image.sh <image> <output-directory>` when Trivy is installed. Publish
and promote only the scanned image, recording its eventual registry digest.
An unavailable scanner/database or unresolved finding fails the check; update
the affected component and rebuild rather than disabling the gate.

Keep developer SDK/shared runtimes on the current supported .NET servicing
release too: package versions and a current container do not update the host
used by `dotnet run` or fleet tests. Record `dotnet --info` during local security
validation. A workstation/runtime installation and actual CI run remain
operator actions; repository edits alone do not apply those updates.

### Wolverine codegen

Wolverine generates handler plumbing at startup; in dev that happens on
every boot. In Production the host loads the pre-generated code the image
build wrote (`TypeLoadMode.Auto`): faster boot, less memory, and a fork
that publishes without the codegen step still starts - it generates at
boot as dev does, rather than dying with a stale-cache error.

## Incidents

`docs/runbook.md` is the on-call companion to this guide: dead-letter
triage and replay, dependency probes, migration failure recovery, the
restore drill, and the support flows (customer lookup, unsuppression,
impersonation).

## Security posture (what the template enforces vs. what you own)

Enforced in code: forced Postgres RLS on every org-scoped table (an
integration test asserts coverage), the three-gate authz model, HttpOnly
`Secure` cookies (Production floor), same-site-only redirects,
constant-time secret comparison, CSPRNG tokens, envelope-encrypted webhook
secrets, an SSRF floor that rejects private/reserved resolved addresses,
local overload admission, security headers, and correlation IDs
that never leak exception detail to clients.

The gateway reference enforces operational tenant/IP fairness; a standalone API
does not provide fleet-wide request-rate limits. Adopt the reference or verify
your replacement against its acceptance contract.

Your responsibility: TLS termination and HSTS at the proxy; the frontends'
CSPs; a KMS for `Secrets:*` and the data-protection keyring; DNS-rebinding
protection at the webhook egress if your threat model needs it (the
registration-time resolve check is a floor, not a guarantee against a host
that rebinds after validation); and a WAF/DDoS layer. CSRF defence is
layered: SameSite=Lax (strips the cookie from cross-site POSTs), no
state-changing GETs, AND an Origin check that refuses an unsafe
cookie-authenticated request whose Origin doesn't match the host. That
covers browser CSRF without a token dance; add synchronizer tokens only if
a fork needs to accept cookie-authenticated cross-origin posts on purpose. The OpenAPI
spec is served unauthenticated (the console's developer page links it);
gate `/openapi` at the proxy if you treat your API surface as secret.

## Database care

- Keys are UUIDv7 (ADR 35) — no sequences to coordinate across regions later.
- Enable PITR/WAL archiving from day one; the audit trail and outbox are the
  two tables you will most regret losing minutes of.
- The retention purge (worker) is the only thing that deletes audit rows;
  backups are your long-horizon story beyond the entitled window.
- RLS smoke after any infra change: connect as `app_user` without the tenant
  GUC and `SELECT count(*) FROM tenancy.sites` — the answer must be 0 rows,
  not an error and not data.

## Observability (ADR 33)

OTLP only, and wired: traces (ASP.NET Core, HttpClient, Wolverine), metrics,
and logs all export wherever the standard `OTEL_EXPORTER_OTLP_ENDPOINT` /
`OTEL_EXPORTER_OTLP_HEADERS` env vars point — the Aspire dashboard in dev
(it injects those vars), any collector in production; nothing exports when
they are unset. Services are named `locintel-api` / `locintel-worker`. Keep
tenant, site, and actor on traces and logs as baggage — **never metric
labels**; a metric with an org label is a cardinality bomb and a
cross-tenant side channel at once.

## Degradation stories (what breaks when a dependency is down)

| Down | Effect | What still works |
|---|---|---|
| SMTP / email provider | Contact links and resets queue in the outbox and retry — the contact tier can't *start* sessions; delivery resumes on recovery | Existing sessions, console, public app |
| WorkOS | No new logins, no invitations, no portal links | Existing cookie sessions (server-side session records are the authority), API keys, public app |
| Stripe | No checkout/portal; webhooks retry from Stripe's side | **All entitlements** — evaluation never leaves the process (ADR 10); paid state converges on recovery |
| Object store | Uploads, downloads, exports fail | Everything else; export messages retry via the outbox |
| Postgres | Everything | Nothing — this is the one true dependency; invest in HA here first |
