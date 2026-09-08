# LocIntel

The crime-intelligence product fork of the location/site-based SaaS template.

A vertically sliced modular monolith. C# 14 / .NET 10 / EF Core 10 / PostgreSQL backend; TanStack +
TypeScript frontend (Start for the public app, SPA for the console); WorkOS behind
an OIDC-generic auth seam; Wolverine for mediation, messaging, and the outbox.

**Tenancy has two axes**: ownership (which org owns a row) and scope (which site or
subtree it belongs to). Every request passes **three gates**, in order:

1. **Entitlement** — does the org's plan include this capability? Fails as 402/upsell.
2. **Grant** — does the principal hold `(domain, action)`? Fails as 403.
3. **Scope** — over which hierarchy nodes does the grant apply? Never fails — it
   *filters*. `scopeFor(principal, action)` returns a `NodeScope` that repositories
   require as an argument.

## Architectural decisions

Settled decisions live in `docs/decisions/` (one ADR each, indexed in its
README). **Consult them before proposing structural changes.**
The product plan being built on this fork is `docs/product/crime-intelligence-blueprint.md`;
record build-time decisions in its decisions log. Decisions marked
`pinned: true` are expensive to reverse once data exists — do not contradict them
without the maintainer explicitly reopening the decision.

## Invariants that cannot be automated (so hold them yourself)

Rules below are judgment calls a compiler can't catch. Everything mechanical is
enforced by analyzers, architecture tests, RLS, and CI — trust those layers and
don't restate them here.

- **Org is never ambient.** Org id (and region) appear explicitly on every cache
  key, message envelope, background job, and audit record. If you write code that
  assumes "the current org," you are wrong — resolve it from the principal or the
  envelope.
- **No ambient connection string.** All data access resolves its context from the
  org's region, even while there is only one region (ADR 35).
- **Every new entity declares its deletion tier** (ADR 25): lifecycle status
  (sites, orgs, memberships), soft-delete with restore (user content), or hard
  delete (join rows, tokens, ephemera). A site is *closed*, never deleted.
- **Every temporal column is one of four kinds** (ADR 26/27): UTC instant
  (`timestamptz`), wall-clock recurring rule (RRULE with TZID), stamped site-local
  business date, or materialized occurrence. Name and comment which one it is.
- **Fact tables stamp context at write time**: ancestor path keyed by
  `hierarchy_id` (ADR 02/04) and site-local business date (ADR 26).
- **RRULE expansion is server-authoritative** and happens in the site's IANA zone;
  the client only displays (ADR 27).
- **New tenant-scoped tables need an RLS policy** in the same migration. CI asserts
  coverage, but write it up front — use the `new-migration` skill.
- **Every row has exactly one owning org** (ADR 48, pinned). No counterparty
  columns, recipient side tables, or `published` flags on a tenant's row.
  Another org that needs to see or act on it gets ITS OWN `IOrgScoped` row,
  materialized through the outbox (the `org_directory` pattern). Authority
  is which object you own plus which commands exist - never an RLS clause.
- **Keys are UUIDv7**, never database sequences (ADR 35 preconditions).
- **Spatial columns are `geography` in SRID 4326** (ADR 50), NetTopologySuite
  types in code, geometry only inside tile generation. `sites.location` is
  derived from latitude/longitude on save - never set it by hand.
- **Under RLS only leakproof operators reach an index** (ADR 51): PostGIS,
  ltree and ILIKE predicates are sequential scans for `app_user`. Hot
  predicates on tenant tables go through derived btree keys (`cell`,
  `path_text`, search terms, keyset cursors); prove a new one with EXPLAIN as
  `app_user`, never as the migrate role (`ScaleIndexTests` shows how).
- **Per-process state must declare its scope** (ADR 52/54): idempotency
  keys and sweep leases remain shared through Postgres; request fairness
  belongs to the gateway, independent of billing. Local concurrency limits
  protect each process. Document intentionally local caches in
  `docs/production.md`; prove shared behavior with the fleet/gateway suites.
- **No session state on a database connection** (ADR 53): the tenant
  variable is `SET LOCAL` per transaction, never `set_config(..., false)`;
  a transaction-mode pooler hands connections between tenants between
  transactions. `SessionStateTests` refuses the session-scoped shapes.
- **Never put tenant/site/actor on metric labels** — traces and logs only, as
  baggage (ADR 33).
- **Frontend imports UI only from `@locintel/ui`**, never `components/ui/*` directly
  (ADR 20). Capability keys come from codegen, never hand-typed strings (ADR 16).
- **Guests are principals.** No anonymous code paths — the principal pipeline
  builds a tenant-scoped Guest from the request host before authn (ADR 07).
- **The three gates are one call, never a ceremony.** Endpoints use
  `Gate.RequireAsync` (any principal), `RequireUserAsync` (humans only),
  or `RequireOperatorAsync`, then `gate.ToResult()`; gate 1 answers through
  `GateResults.LimitReached`/`FeatureOff`. No principal is 401, a missing
  grant is 403, scope filters. An architecture test refuses the old inline
  shape - 67 sites had answered 401 where the contract says 403.
- **One Wolverine handler class per message type**, named `<Message>Handler`.
  A single class with multiple `Handle` overloads is silently NOT discovered —
  messages publish into the void with no error and no dead letter.
- **Wolverine codegen rules** (fail at first request, not at build): register
  services by TYPE (`AddScoped<IFoo, Foo>()`), never via lambda factories —
  opaque factories force service location and Wolverine refuses them. Any
  transactional endpoint/handler whose dependency chain touches more than one DbContext
  (injecting `IScopeResolver` is enough — it uses IdentityDbContext) must
  declare its transaction owner: `[Transactional(typeof(TenancyDbContext))]`.
  Name a context the chain does NOT supply and the host dies at startup, so
  every test fails at 1ms with no usable message — `TransactionalAttributeTests`
  turns that into a named build failure. An endpoint returning `IResult` with
  no `[ProducesResponseType(typeof(T), 200)]` generates an untyped client:
  echo a typed state record rather than returning 204 (the ratchet enforces it).
- **Contract consumption follows the ladder** (ADR 37): Tenancy consumes no
  module's contracts; Identity reads org data only via its org_directory read
  model; every org-writing flow publishes `OrganizationUpserted`. Consuming a
  contract implemented above your module creates an extraction-blocking cycle.
- **Tenant resolution is read-time, always.** Wolverine's transactional frames
  open the DB connection before middleware or handler bodies run — anything
  the RLS interceptor needs must be answerable lazily from whatever scope asks
  (HTTP claims, or the message envelope via IMessageContext). Never "set" the
  tenant at a pipeline point.
- **Never hand api/worker owner credentials** (ADR 38). Migrations belong to
  the migrate role; api/worker connect as app_user, or RLS is silently inert.
- **Unit tests are pure logic** — no mocks, fakes, or persistence substitutes
  (arch-enforced). Infrastructure behavior is integration-proven, full stop.
  New endpoints declare typed responses — an untyped one generates a client
  that looks safe while accepting anything.
- **One primary object per file, named for it** — supporting types get their
  own files; no grab-bags (ADR 38; new code — existing slices grandfathered).

## Agent meta-rules

- Don't mark work complete until the applicable checks pass. If a check isn't
  implemented yet, say so — never imply it ran.
- If a request conflicts with these rules, surface the conflict; never
  silently weaken the rule.

## Workflows

- New vertical slice / module: use the **new-module** skill. Never hand-roll a
  module; each one needs its own schema, DbContext, migration history, Wolverine
  registration, arch-test registration, and test fixtures.
- New EF migration: use the **new-migration** skill (carries the RLS checklist).
- Applied migrations are immutable — add a new migration instead of editing one
  (a hook enforces this).

## Commands

- Build: `dotnet build LocIntel.slnx` (Aspire CLI must be on PATH: `~/.aspire/bin`)
- Architecture tests (fast, run after any cross-module change):
  `dotnet test tests/LocIntel.ArchitectureTests`
- Unit tests (pure logic): `dotnet test tests/LocIntel.Platform.UnitTests`
- Tenant-isolation golden suite (needs Docker; Testcontainers Postgres):
  `dotnet test tests/LocIntel.IntegrationTests` — or one deterministic shard,
  exactly as CI runs it: `tools/run-integration-shard.sh 1 2`
- Password-less local boot (browser smoke runs, no credentials typed):
  `LOCINTEL_AUTH=local aspire run` — skips the WorkOS emulator, uses the local
  provider, and `GET /auth/login?hint=<email>` signs in directly. Dev seeds
  (alice, operator) are keyed to whichever provider is active.
- Local dev: `aspire run` from `src/LocIntel.AppHost` (Postgres + WorkOS emulator + migrate → api + worker + dashboard). Dev login: alice@acme.test / test123 (seeded in `workos-emulate.config.yaml`). Caught mail (contact links, resets): `GET /dev/mail` on the api. Localhost quirk: cookies ignore ports, so a console session bleeds into `localhost:5174` — prod subdomains don't have this.
- Migrations: `dotnet ef migrations add <Name> --project src/Modules/<Module> --startup-project src/Modules/<Module>` (see new-migration skill)
- Format: `dotnet csharpier format .`
- Adding a public-app route: create the file, then `pnpm --filter public run
  routes` — `routeTree.gen.ts` is committed (so a fresh checkout typechecks)
  and goes stale otherwise; CI fails on drift, like `openapi.json`.
- Coverage, report-only (no floor yet): run the suites with
  `--collect:"XPlat Code Coverage"` (`COVERAGE=1 tools/run-integration-shard.sh 1 2`),
  then `tools/coverage-report.sh` - by tier (job) and module (assembly).
- Browser + a11y suite (Docker, Playwright; the same script CI runs):
  `tools/e2e-stack.sh` boots Postgres, migrate + api with the local provider,
  the built console and public SSR servers, then Playwright with an axe pass per page.
- Fleet suite (Docker; N api + N worker behind a proxy on one host):
  `tools/replica-stack.sh 2` runs `tests/LocIntel.FleetTests`;
  `tools/replica-stack.sh 4 --bench` runs the load baseline through the proxy.
- Frontend (web/): `pnpm install`, `pnpm typecheck`, `pnpm build`,
  `pnpm dev:console` (SPA, proxies to the API), `pnpm dev:public` (Start/SSR)
- ReUI/shadcn installs run FROM `web/packages/ui` (the CLI reads
  `components.json` and `.env.local` only from its cwd): `pnpm dlx
  shadcn@latest add @reui/<item>`; then re-export from `src/index.ts`, and
  rewrite `@/` imports to relative - Vite in the apps has no `@` alias.
- Contract codegen (ADR 16): run the integration tests (snapshots
  `web/packages/api/openapi.json`), then `pnpm codegen:api` (types) and
  `pnpm codegen:keys` (capability/entitlement unions). A dirty openapi.json
  after tests means the contract changed - review it like code.
- New module: `python3 tools/new-module.py <Name>` (prints the wiring list)
- Fork init: `python3 tools/init.py <ProductName>` (one-way rename; also adds
  the `template` remote and creates `template-renamed` at the init commit)
- Pull the template forward into a fork: `tools/sync-upstream.sh` (ADR 36).
  It replays upstream through `init.py --snapshot` (rename only — no
  verification, no git bootstrap; the fork bootstrap inside a worktree would
  corrupt the branch the sync is building). Covered by
  `tests/sync-upstream.test.sh`, which syncs twice.

## For forks

`.claude/` and this file are **product surface**: forks inherit these guardrails.
The init script (ADR 36) rewrites names here too. Keep this file under ~150 lines;
when an AI session makes a mistake a test could have caught, write the test — only
add a line here when no test could catch it.
