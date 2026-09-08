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
| `migrate` | Applies all eight modules' EF migrations with **owner** credentials, provisions the unprivileged `app_user` role, reassigns schema ownership, exits | Once per deploy, before the others |
| `api` | HTTP surface (Wolverine endpoints, auth, webhooks) | 1+ replicas |
| `worker` | Outbox delivery, scheduled retries, occurrence materialization, retention purge, idempotency cleanup | 1+ replicas — every recurring sweep is leased per period in `platform.sweep_runs` (first replica to claim `(sweep, period)` runs it, the rest skip), so replicas never duplicate a sweep |

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
- `api` and `worker` receive the same connection string **plus**
  `Database:AppUser` / `Database:AppPassword`; at boot they rewrite the
  connection string to those credentials and never hold owner access.

This is what makes row-level security real: `app_user` is subject to RLS,
the owner is not. **If api or worker ever connects as the owner, RLS is
silently inert** — every tenant can read every row and nothing errors. The
migrate role provisions `app_user` itself (password comes from
`set_config`, parameterized — check `MigrationRunner.cs` before changing it).

Applied migrations are immutable — new migration, never an edit (a repo hook
enforces this; your CI should too via the round-trip tests).

## Configuration reference

Everything the image reads. Section syntax (`A:B`) maps to env vars as
`A__B` (double underscore).

### Core

| Key | Required | Notes |
|---|---|---|
| `ROLE` | yes | `migrate` \| `api` \| `worker` (default `api`) |
| `Build:Version` | recommended | Stamp it in CI (e.g. the git SHA or tag); surfaces in `/healthz` and the console footer so "what version are you running?" is answerable |
| `ConnectionStrings:locintel` | yes | Owner credentials; rewritten for api/worker. Omitted `Maximum Pool Size` defaults to 20 per pool (or an explicitly larger `Minimum Pool Size`); explicit maximums are preserved |
| `Database:AppUser` / `Database:AppPassword` | api/worker | The RLS-subject identity |
| `Public:HostTemplate` | yes | e.g. `https://{slug}.yourproduct.com` — contact links are minted from this |
| `Proxy:TrustForwardedHeaders` | yes, behind a proxy | Honors `X-Forwarded-Proto/Host/For` from the immediate peer. **Required in the documented topology**: without it, TLS terminates at the proxy, the app sees HTTP, session cookies lose the `Secure` flag and scheme-built URLs (billing returns, SSO portal returns) come out `http://`. Only enable when the proxy strips inbound `X-Forwarded-*` from clients (reverse proxies do). Production also hard-floors cookies to `Secure` regardless — a forgotten flag breaks logins loudly instead of leaking cookies silently |

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
| `Auth:WorkOS:WebhookSecret` | Required for directory sync; unset = webhook deliveries rejected (fail-closed) |

Register the WorkOS webhook endpoint (dsync events) at
`https://api.yourproduct.com/auth/directory/webhook`, and the AuthKit
redirect URI at `https://console.yourproduct.com/auth/callback`.

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
| `Notifications:Sms` | `off` (default, fine in production), `local` (dev/test catcher, refused in production), or `twilio` (set `Notifications:Twilio:AccountSid`, `AuthToken` as secrets, and `From` as an E.164 number or a messaging service id). Members opt in per kind under Account > Notifications; the phone is unverified in v1. |
| `Marketplace:*` | Response windows in minutes per urgency (`EmergencyResponseMinutes` 15, `ScheduledResponseMinutes` 1440, `StandingResponseMinutes` 2880), `InitialRecipients` (10) and `EscalationRecipients` (10) per broadcast, `MaxEscalations` (3). The worker runs the SLA sweep every five minutes. |
| `Intelligence:Provider` | `local` (keyword heuristics, no vendor - allowed in production) or `anthropic` (Claude; set `Intelligence:Anthropic:ApiKey` as a secret, optional `Model` default `claude-opus-5`, `Effort` default `low`). Tenant text leaves the platform only under `anthropic`. |
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
| `Storage:Provider` | `s3` (`Storage:S3:BucketName`, optional `ServiceUrl`/`AccessKey`/`SecretKey`/`ForcePathStyle` for MinIO/R2) or `azure` (`Storage:Azure:ConnectionString`, `ContainerName`); both smoke-tested against MinIO/Azurite. `local` (`Storage:LocalRoot`) is **dev/test only — refuses to boot in Production**: tickets live in process memory and bytes on local disk |
| `Scanner:Provider` | `clamav` (`Scanner:ClamAv:Host`, `Port` default 3310, `TimeoutSeconds` default 60; clamd with TCPSocket enabled) or a fork adapter behind `IVirusScanner`. `eicar` is **dev/test only — refuses to boot in Production**: it reads 128 KiB and knows one signature. A scanner that cannot answer keeps the object quarantined; it never reads as clean |
| `Secrets:Provider` | `kms` (`Secrets:Kms:KeyId`, optional `ServiceUrl`/`AccessKey`/`SecretKey`; ADR 31, LocalStack-tested) or a fork adapter. `local` (`Secrets:LocalMasterKey`, the default when that key is set) is **dev/test only — refuses to boot in Production** |
| `RateLimits:GuestPerMinute` / `RateLimits:UserPerMinute` | Defaults 60 / 300; per-org API quota comes from the entitlement |
| `Impersonation:TtlSeconds` | Support-session length (default 3600) |
| `Api:ExposeOpenApi` | Serve `/openapi/v1.json` (default true; the console developer page links it). Set false to hide the API surface |
| `Webhooks:RetryBaseSeconds` | Outbound webhook backoff base (default suits production; tests shrink it) |
| `Audit:PolicyCacheTtlSeconds` | Per-org audit-policy cache |

### Boot guards

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

Upload safety: S3 tickets sign `If-None-Match: *` and Azure tickets grant only
Create, so clients cannot overwrite existing scanned objects. Fork storage
adapters must enforce the same create-only guarantee. `IObjectStore.GetLengthAsync`
replaces `ExistsAsync`: return actual stored length, null for absence, and let
other provider failures propagate. Completion rejects empty or oversized objects
before publishing a scan. The declared size remains capped at 100 MiB.

The cloud ticket does **not** cap bytes received by the storage service; size is
checked at completion. Rejected or abandoned uploads remain unavailable but can
consume storage. Monitor incomplete uploads and storage spend; deployments needing
a hard ingress quota need provider policy or a bounded upload gateway. Do not
claim that the application admission limit prevents storage-cost abuse.

Configure storage CORS for the actual console origins, PUT/GET, Content-Type,
and provider ticket headers (`If-None-Match` for S3; `x-ms-blob-type` for Azure).
All ticket headers must reach storage. For an existing deployment, previously
issued overwrite-capable URLs are not retroactively revoked: stop issuing old
tickets and allow their 15-minute TTL to expire before trusting the new invariant,
or revoke their signing credentials. Review previously uploaded content if such
URLs were exposed. Validate these controls against the selected live provider;
local evidence uses MinIO and Azurite, not AWS/Azure/R2 accounts.

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
  server-to-server) and reachable from the org subdomains. The API stamps
  `Cache-Control` on `/public/*` (60s) and on `sitemap.xml`/`robots.txt`
  (longer) so a CDN in front of the API or the public app absorbs crawler
  and embed traffic; put the public app behind a CDN to cache the rendered
  HTML too.

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

### Wolverine codegen

Wolverine generates handler plumbing at startup; in dev that happens on
every boot. In Production the host loads the pre-generated code the image
build wrote (`TypeLoadMode.Auto`): faster boot, less memory, and a fork
that publishes without the codegen step still starts - it generates at
boot as dev does, rather than dying with a stale-cache error.

`CodeGeneration:Mode` can explicitly select `Dynamic`, `Auto`, or `Static`.
For verified pre-generated images, use `Static` to refuse missing handlers rather
than paying an unexpected runtime-compilation cost. The image smoke check now
requires Static loading under a 512 MiB cap. Ordinary Development stays Dynamic;
the Production default remains Auto for compatibility. This setting does not
change the environment or relax any production-provider guard.

The local container profile uses the same strict loading path with synthetic
local adapters. See [memory diagnostics](memory-diagnostics.md) for measured
RSS, managed memory, CPU, and the distinction from full production acceptance.

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
per-tenant + per-key rate limits, security headers, and correlation IDs
that never leak exception detail to clients.

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
