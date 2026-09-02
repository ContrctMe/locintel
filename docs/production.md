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
| `worker` | Outbox delivery, scheduled retries, occurrence materialization, retention purge, idempotency cleanup | 1+ replicas |

Ordering matters: `api`/`worker` should start (or restart) after `migrate`
exits successfully — the Aspire graph does this with `WaitForCompletion`;
in Kubernetes use an init container or a migration Job; in Compose use
`depends_on: condition: service_completed_successfully`.

`GET /healthz` returns 200 when the process is ready (503 while starting) and
reports its role — wire it to your readiness probe.

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
| `ConnectionStrings:locintel` | yes | Owner credentials; rewritten for api/worker |
| `Database:AppUser` / `Database:AppPassword` | api/worker | The RLS-subject identity |
| `Public:HostTemplate` | yes | e.g. `https://{slug}.yourproduct.com` — contact links are minted from this |
| `Proxy:TrustForwardedHeaders` | yes, behind a proxy | Honors `X-Forwarded-Proto/Host/For` from the immediate peer. **Required in the documented topology**: without it, TLS terminates at the proxy, the app sees HTTP, session cookies lose the `Secure` flag and scheme-built URLs (billing returns, SSO portal returns) come out `http://`. Only enable when the proxy strips inbound `X-Forwarded-*` from clients (reverse proxies do). Production also hard-floors cookies to `Secure` regardless — a forgotten flag breaks logins loudly instead of leaking cookies silently |

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
| `Storage:LocalRoot` | Local disk adapter — swap the `IObjectStore` registration in `Program.cs` to `S3ObjectStore` (`Storage:S3:*`) or `AzureBlobObjectStore` (`Storage:Azure:*`) for production; both are smoke-tested against MinIO/Azurite |
| `Secrets:LocalMasterKey` | **Dev/test only — refuses to boot in Production.** Register a KMS adapter (`KmsKeyWrapper`, ADR 31) |
| `RateLimits:GuestPerMinute` / `RateLimits:UserPerMinute` | Defaults 60 / 300; per-org API quota comes from the entitlement |
| `Impersonation:TtlSeconds` | Support-session length (default 3600) |
| `Notifications:Sms` | `off` (default, and the only value allowed in Production without a fork adapter) or `local` (dev catcher). SMS is a SEAM: the template ships the port and an off transport, never a routing or consent policy |
| `Api:ExposeOpenApi` | Serve `/openapi/v1.json` (default true; the console developer page links it). Set false to hide the API surface |
| `Webhooks:RetryBaseSeconds` | Outbound webhook backoff base (default suits production; tests shrink it) |
| `Audit:PolicyCacheTtlSeconds` | Per-org audit-policy cache |

### Boot guards

The image **refuses to start in Production** with any dev-only adapter
still selected: local auth, local billing, local notifications, or the local
key wrapper. Treat a failed boot here as the guard working, not a bug.

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

## Before go-live: Wolverine codegen

Wolverine generates handler plumbing at startup; the default Dynamic mode
(fine in dev) does that work on every boot and says so in the log. For
production images, pre-build the generated types
(`dotnet run -- codegen write` during the image build, and set
`opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static` under a
production check) to cut startup time and memory. The template leaves
Dynamic as the default because Static with a stale cache fails the boot -
adopt it together with your CI image build, not before.

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
