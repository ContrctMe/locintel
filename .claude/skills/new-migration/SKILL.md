---
name: new-migration
description: Create an EF Core migration for a module, including the RLS policy checklist for tenant-scoped tables. Use for any schema change, new table, or migration work.
---

# New migration

Migrations are per-module (each DbContext has its own history in its own schema,
ADR 17). Applied migrations are immutable - a hook denies edits to existing
migration files; always add a new one.

## Steps

1. Identify the owning module and its DbContext. A schema change spanning two
   modules is two migrations - and a design smell worth flagging.
2. Create it:
   ```bash
   dotnet ef migrations add <Name> --project src/Modules/LocIntel.Modules.<Name> --startup-project src/Modules/LocIntel.Modules.<Name> --context <Name>DbContext
   ```
3. **RLS checklist - every new tenant-scoped table.** Use the helper for the
   shape you have; never hand-write the policy SQL:
   - Single owner (the common case): `migrationBuilder.EnableTenantRls(schema, table)`
     on an entity implementing `IOrgScoped`.
   - **Two-party** (owner + counterparty: a request and its vendor, a shared
     case): `EnableTwoPartyRls(schema, table, "counterparty_org_id")` on an
     entity implementing `ITwoPartyScoped`. Both sides read and write; the
     WITH CHECK stops a third org forging a row between two others.
   - **Published catalog** (owner writes, everyone reads once published):
     `EnablePublishedCatalogRls(schema, table)` on `IPublishedCatalogScoped`.
     This is deliberately TWO policies - a single `FOR ALL` policy allowing
     published reads would also allow published *writes* by any tenant.
   - **Recursion rule.** A policy may reference ANOTHER table, never its own -
     a self-referencing policy recurses and the query fails at runtime. For
     "can this org see it through a grant", anchor visibility on one
     single-owner access table and have the other tables' policies `EXISTS`
     against it.
   - Raw SQL goes in the migration via `migrationBuilder.Sql(...)`.
   - Platform-global tables (no org_id) are the exception - say so explicitly in
     a migration comment AND add them to `RlsCoverageTests.PlatformGlobal`,
     which is the assertion that would otherwise fail.
4. **Column checklist:**
   - UUIDv7 keys, never sequences/identity for entity ids (ADR 35)
   - timestamptz for instants; document which temporal kind each column is (ADR 26)
   - soft-delete tier entities: `deleted_at` + partial unique indexes where needed (ADR 25)
5. **`Down()` is maintained, not decorative** (ADR 38): `MigrationRoundTripTests`
   applies every migration, reverts to 0, and applies again — a `Down()` that
   does not truly reverse `Up()` fails the build. Never drop the module's
   schema in `Down()`; it holds the migration history table. **Drop
   cross-table policies BEFORE the tables they reference**: a `Down()` that
   drops a table while another table's policy still `EXISTS` against it fails
   (a fork hit exactly this).
6. Review generated SQL with `dotnet ef migrations script` before considering it done.
7. Run the module's tests plus the tenant-isolation suite (the round-trip
   test runs with the integration suite).
