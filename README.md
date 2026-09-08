# LocIntel

A forkable template for **location/site-based multi-tenant SaaS** — the
platform machinery every product in this category rebuilds, finished once:
tenancy with real isolation, plans and metering, roles and scopes, schedules
that respect time zones, audit that holds up, and the operational seams
(auth, billing, email, storage) behind swappable adapters.

You don't install LocIntel. You **fork it, rename it, and build your vertical
on top** — the template stays out of your domain and owns everything beneath
it.

## What's in the box

- **Vertically sliced modular monolith** — C# / .NET 10 / EF Core / PostgreSQL,
  Wolverine for mediation, messaging, and the transactional outbox. Ten
  modules (tenancy, identity, entitlements, audit, storage, ingest,
  checklists, spatial, reporting + platform; reporting is in progress),
  each with its own schema, DbContext, and migration history.
- **Two-axis tenancy, three gates.** Every request passes entitlement (402,
  upsell) → grant (403) → scope (never fails — it *filters*). Row-level
  security enforces org isolation at the database, with the tenant GUC set on
  each transaction by construction (including implicit read transactions).
- **Principals all the way down**: users (WorkOS AuthKit behind an
  OIDC-generic seam), magic-link contacts, API keys as service principals,
  and tenant-scoped guests — no anonymous code paths.
- **Sites, hierarchy, and time**: org-defined hierarchy (ltree), sites with
  IANA time zones, RRULE schedules with server-side expansion, materialized
  occurrences, holiday closures, and a public locator app.
- **Money and limits**: plan catalog, four entitlement shapes with per-code
  limit policies, metered usage, hosted checkout/portal via Stripe (adapter),
  operator custody for exceptions.
- **Audit as a feature**: CDC diffs, domain events, authz decisions, access
  log; per-org retention; outbound signed webhooks; tenant-facing JSONL
  export.
- **Enterprise**: SSO + SCIM directory sync via the WorkOS admin portal,
  operator impersonation (time-boxed, audited both ways), org lifecycle
  (suspend, offboard, export).
- **Frontends**: console SPA and a public SSR app (TanStack), typed API
  client and capability keys generated from the OpenAPI contract.

## Quickstart

Prereqs: .NET 10 SDK, [Aspire CLI](https://learn.microsoft.com/dotnet/aspire)
(`~/.aspire/bin` on PATH), Docker, Node + pnpm.

```bash
cd src/LocIntel.AppHost && aspire run
```

That boots Postgres, the WorkOS emulator, the migration runner, api + worker,
both frontends, and the Aspire dashboard. Sign in at `http://localhost:5173`
as `alice@acme.test` / `test123`. Caught mail (contact links, resets):
`GET /dev/mail` on the API. The public app for the seeded org is
`http://acme-dev.localhost:5174`.

```bash
dotnet build LocIntel.slnx                       # build everything
dotnet test tests/LocIntel.ArchitectureTests     # fast structural checks
dotnet test tests/LocIntel.Platform.UnitTests    # pure logic
dotnet test tests/LocIntel.IntegrationTests      # Testcontainers Postgres
tools/scale-baseline.sh                         # optional sustained mixed-workload/bundle baseline
cd web && pnpm install && pnpm typecheck        # frontends
```

## Forking

```bash
python3 tools/init.py YourProductName   # one-way rename (ADR 36)
python3 tools/new-module.py Booking     # scaffold a vertical slice
```

The rules that keep the template's guarantees intact live in
[CLAUDE.md](CLAUDE.md) (agents and humans alike), and every structural
decision has an ADR in [docs/decisions/](docs/decisions/README.md), indexed
there with its status. **Read an ADR before contradicting it**; the pinned ones are
expensive to reverse once data exists.

Deploying a fork to production: [docs/production.md](docs/production.md) —
topology, the database role split, the full configuration reference, and the
guards that refuse to boot until dev-only adapters are replaced.

Current engineering maturity and the prioritized path to production readiness:
[software maturity review and forward roadmap](docs/software-maturity-review-details.md)
(hosted CI verified; unreproduced reliability risks provisionally accepted for staging, not resolved;
current local architecture hardening covers strict site-capacity reservations and transaction conventions;
deployment acceptance remains open; maintained by the project maintainers; last updated 2026-09-07).

Performance, scalability evidence, and the plan to validate 1,000 requests/sec:
[performance and scalability assessment](docs/performance-and-scalability-assessment.md)
(theoretical limits, measured results, open risks, and deployment acceptance criteria; 2026-09-07).
Implementation and new experiments: [capacity validation](docs/capacity-validation.md).
Request-rate policy replacement: [gateway operational fairness](docs/gateway-fairness.md)
(accepted direction and verification ledger; implementation in progress).

Disposable cloud testing and provisional production target:
[DigitalOcean deployment plan](docs/digitalocean-test-environment.md)
(proposed topology, failure gates, campaign cost and teardown; 2026-09-07).

Public authentication failure behavior and verification:
[public session recovery](docs/public-session-recovery.md).

Checklist fleet navigation and failure behavior:
[checklist site selection](docs/checklist-site-selection.md).

Site-library report generation, persistent site PDFs, shared site permissions and run history
for forks: [ADR 56](docs/decisions/0056-reporting-execution-and-delivery.md) settles
execution, accounting and lifecycle; [reporting](docs/reporting.md) is the fork
authoring guide (PDFs, maps/photos, bulk ZIPs, console workflow and entitlement
quotas). Live-provider and target-deployment qualification remain pending.

## Layout

```
src/LocIntel.AppHost/       Aspire dev orchestration (dev-only)
src/LocIntel.Api/           the deployable: one image, ROLE = migrate|api|worker
src/LocIntel.Platform/      kernel seams: principals, scopes, entitlements, ports
src/LocIntel.Contracts/     cross-module messages + read-model contracts (ADR 37)
src/Modules/*/             vertical slices, one schema + migration history each
src/Integrations/*/        adapters: WorkOS, Stripe, SMTP, S3, Azure Blob
web/apps/console/          tenant console (SPA)
web/apps/public/           public locator ({slug}.yourdomain, SSR)
web/packages/api/          generated client + capability keys (never hand-edit)
tools/                     init.py, new-module.py, run-integration-shard.sh
```

Security: [assessment](docs/security-assessment.md) and [remediation ledger](docs/security-remediation.md), including deployment acceptance and verification evidence.

Shared import limits, resource lookup behavior, and reproducible fork checks: [Engineering maintenance](docs/engineering-maintenance.md).
