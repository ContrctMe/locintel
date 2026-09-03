# Architecture review, 2026-09-02

Run with the `improve-codebase-architecture` skill (deep-module vocabulary:
module, interface, depth, seam, adapter, leverage, locality). Scope was set
by history: the composition root (35 touches), Tenancy and Identity (~105
each), and the test fixture (21). Six deepening candidates came out; each
was investigated independently, in priority order, and implemented where it
survived the investigation. This file is the durable record - especially of
what was declined and why, so the next review does not re-suggest it.

| # | Candidate | Verdict | Commit |
| --- | --- | --- | --- |
| 1 | Collapse the three-gate guard ceremony | **Done** | `820181f` |
| 2 | One module for what a session means | **Done** | `97dc766` |
| 3 | Derive persistence wiring from the catalog | **Done** | `6226e8d` |
| 4 | Close the test back doors | **Partial** - the shared definition; not the observation seams | `1aa047d` |
| 5 | One adapter-selection rule for five seams | **Declined** | - |
| 6 | Column naming by convention | **Declined** after a probe | - |

Also from the review: the wait-hygiene guard was found blind to compound
loop conditions and missed 37 waits; it now sees them and holds a
shrink-only ratchet (`7b69f23`).

## 1. The guard ceremony - done

The gates were interface, not implementation: dozens of hand-written
principal guards in at least five shapes, 67 `CanAsync` sites, and the
reference slice propagating a sixth. They disagreed with the contract: a
missing grant is 403 (CLAUDE.md, `GatesTests`), and 95 sites answered 401.

`Gate` (Platform) is the decision as data; `GateResults` (Contracts) is the
one place it becomes a status code and carries the single 402 body that
gate 1 had grown four shapes of. 98 sites converted; 17 guard-only "signed
in?" checks kept, since a 401 for no principal is the contract. Reads that
narrow silently still call `ScopeForAsync` and filter. `GateTests` proves
the contract once at the seam; `GateCeremonyTests` refuses the inline shape.

**Behaviour change:** a signed-in principal lacking a grant now gets 403.
Eighteen assertions encoded the drift and were flipped, each checked to be
a signed-in case.

## 2. The session module - done

`LocIntelClaims.ContactId` exists (it was a literal in the writer and the
reader, absent from the constants). `BuildContactClaimsPrincipal` sits
beside the user issuer. `MembershipQueries.DefaultOrgAsync` is the next-org
rule that three sites carried verbatim. Both rate-limit partitions key off
`IPrincipalAccessor.Current` - the Principal the endpoints see - instead of
re-parsing claims, which is how API keys had fallen into the guest bucket.

Not done: the promised claims-round-trip unit test. The resolver needs an
`HttpContext`, and the unit project is pure logic by architecture rule; the
seam is covered by an integration test through a contact session.

## 3. Persistence from one place - done

`ModulePersistence.AddModuleDbContext<TContext>(schema, audited)` is the
block seven modules carried verbatim; each is one line, the generator emits
the one line, and the fixture uses its generic factory. The ADR 35 region
change is now one edit.

## 4. Test back doors - partial, and why

**Done:** `RoleGrant.Wildcard(role)`. The grant that means "all authority
in this org" was spelled by hand in founder provisioning, the dev bootstrap
and the fixture.

**Declined - shared org bootstrap.** The product rule
(`EnsureMembershipAsync`: the FIRST member becomes Owner) cannot stand in
for the fixture, which deliberately seeds several Owners per org and a
role-less viewer. A shared bootstrap would need a second interface for
exactly what the fixture does today: indirection, not depth.

**Declined - observation through module interfaces.** `QueryAudit` (x11),
`QueryWindows` (x4) and the raw connections verify persistence facts -
row-level audit diffs, projected occurrence rows, partition routing - that
the product API does not and should not expose. Routing them through HTTP
would weaken the assertions to what the API happens to return. They stay
as explicit, named fixture helpers.

## 5. Adapter selection - declined

Five seams in `Program.cs` each guard `local` with
`when !IsProduction()` and throw citing an ADR. On inspection the shared
part is that three-line guard; the registrations around it differ in shape
(options binding, a decorator, a factory). A selector module would have an
interface as wide as the five bodies it wraps - the deletion test fails:
delete it and five three-line guards reappear, which is what is there now.
Revisit only if a sixth seam arrives with the same shape as one of these.

## 6. Column naming by convention - declined after a probe

`EFCore.NamingConventions 10.0.1` targets EF Core 10. Applied to Tenancy
and scaffolded a probe migration: **29 operations** - 13 index renames
(`IX_sites_path` to `ix_sites_path`) and 8 primary-key drop/re-adds - and
**zero column renames**. So the ~290 `HasColumnName` calls are redundant
with the convention, but the package renames indexes and constraints too,
which means a rename migration across every table in every module. That
is DDL churn, the opposite of the locintel. A column-only custom convention
would recover the navigability win at the cost of new EF plumbing for that
alone; not worth it now. The probe was deleted; nothing landed.

## Checked and not shallow

`IScopeResolver` (two adapters at one seam, memoized, audit as decorator),
`ModuleDbContext` (the deep module candidate 6 would have extended),
`PublishForOrgAsync`/`AuditAsync` (thin, but the envelope-tenant rule is
concentrated), and the ADR 37 ladder seams (`IAuthProvider`,
`ISiteDirectory`, `IOrganizationLookup` - two adapters each).
