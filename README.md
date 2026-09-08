# LocIntel

## Product and deployment status

LocIntel is the crime-intelligence product built on the Premise platform below:
incidents, people/vehicles, investigations, alerts, patrols, intelligence sharing,
and a services marketplace. See the [product blueprint](docs/product/crime-intelligence-blueprint.md).
The inherited platform review is not a fresh review of all LocIntel features.

Local container preparation is active; no hosted deployment is approved or running
as part of this work. See [container staging preparation](docs/container-staging.md)
for verified checks, remaining acceptance criteria, and the budget baseline
(owner: project maintainers; updated 2026-09-05).
Shared-budget browser rate limiting remains a verification blocker. The corrected
all-in-one integration suite previously passed under a 4 GiB cap (302 passed, one skip,
no OOM), although memory headroom remains tight.
See [bounded-memory findings](docs/memory-diagnostics.md) for the reproduced
fixture retention and its verified fix. The full local stack passed 778 measured
requests, real scanning/reload, and session/file persistence after recreation.
Those first measurements used Dynamic handler compilation. A controlled follow-up
with pre-generated handlers passed at a **512 MiB API cap**, peaking near **254 MiB
process RSS**, versus 1,722 MiB with Dynamic loading. The local profile now selects
Static loading; AV remains separate. Longer-soak sizing and restore remain open.
The [concurrency/soak follow-up](docs/concurrency-soak.md) exposed starvation
at the default 20-connection pool. A diagnostic pool-64, 30-minute soak passed
11,940 workload requests at a 241 MiB API peak. Bounded HTTP admission now passes
the small-pool regression, focused checks and default-pool ramp through 40 clients
(480/480, 237 ms p95). The admission-only two-hour soak completed 44,760 workload
requests without errors or OOM events; API peak was 306 MiB. One health probe timed
out, and file-backed memory kept growing despite stable late anonymous memory.
Overlapping regression work confounds its tail latency; sizing remains provisional.
The [transaction-lifetime review](docs/transaction-lifetimes.md) distinguishes
necessary atomic writes from unnecessary read transactions. Seventeen reviewed reads
now opt out of eager transactions. Scans, preview reads and connector fetches release
connections before external work; conditional completion preserves atomic writes.
Preview publication retains an erasure-coordination lock. Incident creation uses
a single deferred commit; signup and evidence issuance are POST-only. Targeted
integration, Static-handler and the new flows in all three browser engines pass.
The broader sharded rerun recorded 326 passes, one readiness failure and one skip;
one shard hung after test completion. Collectibility passed on subsequent reruns.
Three diagnostic replays of the failing shard passed and exited cleanly; the original
readiness/hang cause remains unconfirmed, with better failure capture now available.
The new-image two-hour comparison completed: 47,560 soak requests and all 3,819
health probes passed, with no OOMs and 186 ms p95. API cgroup memory peaked at
317 MiB within 512 MiB, but continued file-backed growth prevents claiming a
stable footprint. Sampled pool usage peaked at 10/20. Follow-up traced the dominant
growth to page cache from the local upload adapter: every upload added exactly
its page-rounded size. A no-collector control released and reloaded only the new
files' cache with byte checks intact. Automatic reclaim then released 75.8 MiB
under the unchanged 512 MiB cap without OOM or failed health checks. A local S3
control sent 97.4 MiB directly to MinIO with zero API inactive-cache growth;
MinIO itself ended near 497 MiB. Keep the API cap: representative warm/mixed
production-storage sizing, causal lifetime comparisons and replica/hosted
acceptance remain outstanding.
The mixed-workload S3 follow-up is now in progress, using the unchanged API cap
and a separately capped MinIO service; its final sizing result is pending.
Details and evidence are tracked in the review.

## Inherited platform

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
  every connection open by construction.
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
