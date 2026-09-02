# Template feedback from the LocIntel fork

Written 2026-09-02 after building the crime-intelligence product on the
template (repo `locintel`, branch `main`, commits `b93b3e3..f14782e`).
Purpose: a handoff to whoever maintains the base template. Everything here
is evidence-backed; each item names the fork file or commit to look at, and
the pieces marked **lift** are generic code that belongs upstream as-is.

Read this alongside `docs/product/crime-intelligence-blueprint.md` (the
decisions log has one entry per slice) and `git log --reverse b93b3e3..`.

## Scorecard

| Area | Verdict | Evidence |
| --- | --- | --- |
| Module recipe (generator, schema, RLS, discovery, arch tests) | Strong | 8 new modules, none crossed a boundary; arch tests refused every attempt |
| Three gates + scope query | Strong | New capability = one constant + codegen; two extra gates were one query filter each |
| Contracts ladder + outbox writes | Strong | `FileHoldRequested`, `ImportEntityRequested`, `ShareInvitationRequested` needed no new machinery |
| Safety nets (ratchet, round-trip, RLS coverage) | Strong | Caught 8 untyped endpoints, 1 bad `Down()`, 0 missed policies |
| Multi-org test fixture | Strong | Cross-org features tested with no new infra |
| Fork init script | Weak | Missed `PREMISE_API`; left CI format check broken; reshuffled xUnit order |
| Module wiring | Weak | ~12 manual edits in 6 files per module; the lists rot (Checklists was missing from two) |
| Tenancy shapes beyond single-owner | Missing | Two-party and catalog policies hand-written 4 times |
| Contracts for fact tables | Thin | `SiteInfo` extended 3 times (hierarchy id, coordinates, country) |
| Repeated platform patterns | Missing | Per-org sweeps ×3, audit helpers ×6, test boilerplate ×10 |
| Dev login for automation | Missing | Needed a password until `LOCINTEL_AUTH=local` was added |

## 1. Fork init (`tools/init.py`)

**Problem.** Three things the rename broke that the template did not notice.

- Case-sensitive replace missed `PREMISE_API` (AppHost, both web apps, production guide). Fixed in `13af3b0`.
- Renaming the product namespace changed where it sorts relative to `Microsoft.*`, so csharpier's using order changed in 155 files and `dotnet csharpier check .` (CI) failed from the first commit. Fixed as a mechanical commit `6e429aa`.
- xUnit orders tests within a class by a hash of the fully qualified name. The rename reshuffled it and surfaced two latent order dependencies in the template's own tests: `GatesTests` left a low operator exception as the org's ceiling; `PublicJourneyTests` seeded per test and then built a dictionary keyed by site name. Fixed in `4751088`.

**Proposal.** Init script: replace all case variants; run `dotnet csharpier format .` and the full suite as its last steps; print a diff stat. Template tests: make every test order-independent (restore what it changes; seeds must be idempotent). Consider a CI job that runs the suite with `-p:xunitRandomOrder` or a shuffle to keep it that way.

## 2. Module wiring rots (generator checklist)

**Problem.** The generator prints "7 lines"; a module actually needs about twelve edits across six files: `Program.cs` (register + Wolverine discovery), `MigrationRunner.cs` (migrate + two GRANT lines), `ApiFixture.cs` (migrate + two GRANT lines), `ModuleBoundaryTests.cs`, `RlsCoverageTests.cs` (schema list), `MigrationRoundTripTests.cs` (theory data + switch), `LifecycleTests.cs` (export sections). Evidence that the lists rot: Checklists, the reference vertical, was in neither the round-trip test nor the offboarding export until `843484f` and `6a8cb9f`.

**Proposal.** A module descriptor (`IModuleDescriptor { string Schema; Type DbContext; }`) registered by `Add<Name>Module`, so the host migrates and grants by enumerating descriptors, and the fixture, RLS coverage, round-trip, and export tests derive their lists from the same registry. Until then, the generator should apply the edits itself rather than print them.

## 3. Tenancy shapes beyond single-owner (**lift**)

**Problem.** `IOrgScoped` + `EnableTenantRls` cover one shape. The marketplace needed two-party rows (requester + vendor) and a catalog table readable by every org once published; the network needed one-hop policies through the caller's own access row. Each was a hand-written query filter with the same name as the convention's plus raw SQL in the migration, four times. One `Down()` dropped a table before the policies that referenced it (`c9b8a15`).

**Where it lives.**
- `src/Modules/LocIntel.Modules.Marketplace/Data/MarketplaceDbContext.cs` (two-party and catalog filters) and `Migrations/*_Initial.cs`, `*_Rfq.cs` (policies).
- `src/Modules/LocIntel.Modules.Network/Data/NetworkDbContext.cs` (one-hop through `share_access`, the pattern that avoids policy recursion) and its `Migrations/*_Initial.cs`.

**Proposal.** In Platform: `ITwoPartyScoped { OrgId OrgId; OrgId? CounterpartyOrgId; }` with the convention adding the either-side "Tenant" filter, plus `migrationBuilder.EnableTwoPartyRls(schema, table, counterpartyColumn)` and `EnablePublishedCatalogRls(schema, table, publishedColumn)`. Document the recursion rule: a policy may reference another table, never its own; anchor cross-org visibility on one single-owner table. Add "drop cross-table policies before tables in `Down()`" to the new-migration skill.

## 4. Contracts for fact tables

**Problem.** ADR 2/4 require fact rows to stamp the ancestor path keyed by hierarchy id, but `SiteInfo` shipped with only id, name, path, and zone. It was extended three times: `HierarchyId` (`843484f`), coordinates (`5026160`), `CountryCode` (`f14782e`).

**Proposal.** Ship `SiteInfo` with hierarchy id, coordinates, and address basics from day one. Also add `IIncidentDirectory.ListForDayAsync`-style "rows for a site on a business date" to the pattern for any fact contract, since daily reports were the first thing two modules wanted.

## 5. Wolverine rules that only fail at runtime

- An endpoint returning `Task<IResult>` with no declared 200 type emits an untyped stub; the ratchet catches it, but the fix (echo a typed state record instead of 204) is not written down. See `IncidentMutated` in `src/Modules/LocIntel.Modules.Incidents/Incidents/Api/`.
- `[Transactional(typeof(X))]` on an endpoint that never injects X fails at host startup, so every test in every class fails at 1 ms with no useful message (`9720547`, the entities sweep endpoint).

**Proposal.** Both belong in the new-module skill and CLAUDE.md's Wolverine paragraph. A startup self-check that names the offending endpoint would turn the second into a one-line error.

## 6. Repeated platform patterns (**lift**)

- **Per-org sweep enumerator.** `EntityRetentionService`, `RequestSlaService`, and Tenancy's `HorizonRollService` are the same thirty lines. Lift to Platform as `PerOrgSweepService<TMessage>(interval)`; `PublishForOrgAsync` lives in Tenancy and should move to Platform or Contracts.
- **Domain audit helper.** `IncidentAudit`, `EntityAudit`, `CaseAudit`, `MarketplaceAudit`, `PatrolAudit`, plus inline copies. Lift as an `IMessageBus` extension: `bus.AuditAsync(actor, eventName, payload)`.
- **`ActorRef`** (people and API keys as writers) is already in Platform (`src/LocIntel.Platform/Kernel/ActorRef.cs`); keep it.
- **`CsvParser`** moved from Ingest to `src/LocIntel.Platform/Text/` (`ddc79bc`); keep it there.
- **Test boilerplate.** Hierarchy root + site creation is copied into ten test classes. Add `fixture.EnsureSiteAsync(client, name, zone, lat, lng)` and `fixture.ScopedMemberAsync(email, grants, path)` (the role + assign dance from `EntityTests`).

## 7. Dev login and smoke runs (**lift**)

`LOCINTEL_AUTH=local` (AppHost chooses the provider; `DevBootstrap` keys seeded users as `local_{email}`; `.claude/launch.json` has an `aspire` configuration) made a password-less browser smoke possible (`265b3f2`). The template should ship this: an automated smoke should never type credentials, and the WorkOS emulator path stays for parity checks.

## 8. Papercuts

- Public app's `routeTree.gen.ts` is gitignored and only produced by a build, so a fresh checkout fails `pnpm typecheck` until `pnpm build`.
- `EntitlementCatalog` lets a constant exist without a `Definitions` entry; it fails with a 500 at first use (`e689d17`). A unit test asserting every `const string` has a definition is trivial.
- The members list response shape is undocumented; the console guessed it.
- Attaching a file to anything requires the Files page first; an inline upload control in the ticket flow would remove a step from every attach dialog.
- No dedicated notification transport for SMS or push existed; `ISmsTransport` + `NoSmsTransport` + `LocalSmsCatcher` (`src/LocIntel.Platform/Notifications/`) and the `Kind`/`Sms` extension of `SendOrgNotice` (`4fbfc74`) are generic and can be lifted.

## Status after the first sync (2026-09-02)

Upstream shipped every item above (commits `c72cded`..`2088a25`). The fork
merged them with `tools/sync-upstream.sh`, resolving all 35 conflicts in
upstream's favour and re-seating its own code on the lifted pieces:

| Lifted upstream | What the fork deleted or moved onto it |
|---|---|
| `ModuleCatalog` | seven module lines in Program.cs, MigrationRunner, ApiFixture, three test registries |
| `AuditAsync` / `AuditActor` | `IncidentAudit`, `CaseAudit`, `EntityAudit`, `MarketplaceAudit`, `PatrolAudit`, a private Network helper, seven inline publishes |
| `PerOrgSweepService` | `EntityRetentionService`, `RequestSlaService` (now three lines each) |
| `ITwoPartyScoped` / `EnableTwoPartyRls` | hand filters and policies on `request_events`, `request_recipients`, `quotes` |
| `IPublishedCatalogScoped` | the hand filter on `vendor_profiles` |
| `ISmsTransport` seam | the fork's identical port and catcher; Twilio adapter and preferences stay |
| whole `SiteInfo` | the fork's three extensions |
| `LOCINTEL_AUTH=local` boot | the fork's own AppHost and DevBootstrap variant |

Two things the shapes could not absorb, worth a look upstream: a row
readable by a *list* of counterparties (a broadcast request and its
recipients), and a required counterparty - `ITwoPartyScoped.CounterpartyOrgId`
is nullable by design, so a table whose column is NOT NULL carries a
nullable CLR property plus `IsRequired()`.

One flake surfaced under the shuffled full suite: `AccountTests`' outbox
wait (60 x 100 ms) timed out once with seven more modules feeding the
outbox; the fork widened it to the fixture's own bound (300 x 100 ms).

## Round two (after the first sync, 2026-09-02)

Everything in round one landed and measured well (see the status table
above). These are the second-order items the sync itself surfaced, in
priority order. Items marked **lift** are generic code already in this repo.

### 9. A recipient-list tenancy shape (**lift**)

A row readable by its owner, an optional counterparty, AND every org in a
side table: a broadcast request and its recipients, a share and its
members. The two-party shape cannot express it, so the fork hand-writes it
twice:

- `src/Modules/LocIntel.Modules.Marketplace/Data/MarketplaceDbContext.cs`
  (the `requests` filter, `Recipients.Any(...)`) and the matching policy in
  `Migrations/20260902080547_Rfq.cs`
- `src/Modules/LocIntel.Modules.Network/Data/NetworkDbContext.cs` (the
  `shares` / `share_members` / `shared_bulletins` one-hop reads through
  `share_access`) and `Migrations/20260902042111_Initial.cs`

Proposal: `EnableRecipientListRls(schema, table, recipientsTable,
foreignKeyColumn, recipientOrgColumn)` in `RlsMigrationExtensions`, and a
`ModuleDbContext` helper that adds the matching query filter given the
recipients `DbSet`. The invariant worth asserting in the adversarial test:
the recipients table itself must never reference the parent's policy (a
policy referencing its own table recurses), and the WITH CHECK must not let
a recipient write the parent row before award.

### 10. A required counterparty on `ITwoPartyScoped`

`CounterpartyOrgId` is nullable by design. Two of the three fork tables that
adopted it have a NOT NULL column, so each carries `required OrgId?
CounterpartyOrgId` plus `.IsRequired()` in the context and a non-null read
accessor (`src/Modules/LocIntel.Modules.Marketplace/Requests/Quote.cs`,
`RequestRecipient.cs`). Either a second interface with a non-nullable
property (the filter can accept both) or a note in the skill that this is
the intended pattern.

### 11. Route-tree regeneration is a build side effect

Committing `web/apps/public/src/routeTree.gen.ts` fixed the fresh-checkout
typecheck, but a fork that adds a route (this one added `routes/tips.tsx`)
gets three misleading type errors until a full `vite build` regenerates the
file. Give `web/apps/public/package.json` a `routes` script that runs the
generator alone, and have CI fail on a dirty `routeTree.gen.ts` after
build - the same treatment `openapi.json` already gets.

### 12. Two audit sites the helper missed

`AuditAsync` retired 27 hand-written publishes, but
`src/Modules/LocIntel.Modules.Tenancy/Hierarchy/Endpoints.cs` and
`src/Modules/LocIntel.Modules.Tenancy/Sites/ClosureEndpoints.cs` still spell
the `locintel-actor-tier` headers by hand (`Audit/Handlers.cs` reads them,
which is fine). An architecture test that forbids the header literal
outside Platform, Contracts and the composition root keeps it retired.

### 13. Outbox waits are per-test loops with per-test bounds

32 `for (var i = 0; i < N; i++) { ...; await Task.Delay(100); }` loops in
`tests/LocIntel.IntegrationTests`, with N ranging 20-100. The shuffled full
suite exposed one at 60 (`AccountTests`) that a fork's extra outbox traffic
pushed past its bound. One fixture helper, `WaitUntilAsync(Func<Task<bool>>)`
with a single generous bound and a message naming the predicate, replaces
them all and gives a timeout a readable failure instead of a downstream
assert.

### 14. Ship the sync story (**lift**)

ADR 36 calls the fork one-way. `tools/sync-upstream.sh` in this repo is the
missing half: it renames each upstream snapshot with `tools/init.py` onto a
`template-renamed` branch parented on the fork's init commit, then merges,
so conflicts appear only where both sides changed the same lines (35 of 111
touched files on the first sync, most of them upstream's own audit
cleanup). It has no product-specific logic - the name comes from the
`.slnx`. Lift it, amend ADR 36, and have `init.py` do the bootstrap it
currently documents by hand: add the source as the `template` remote and
create `template-renamed` at the init commit.

## Suggested prompt for the template session

> Read `/Users/jarod/coding/locintel/docs/template-feedback.md`, section "Round two" (items 9-14; round one is already merged). For each item marked **lift**, inspect the named files in that repo and port the generic parts into the template's Platform, Contracts or tools with tests; for the rest, make the change the item proposes. Keep the suite shuffled and green after each change, and note in the commit message which item it closes.
