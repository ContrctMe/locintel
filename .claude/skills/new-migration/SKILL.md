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
3. **RLS checklist - every new tenant-scoped table** (ADR 48):
   - The entity implements `IOrgScoped` and the migration calls
     `migrationBuilder.EnableTenantRls(schema, table)`. **That is the only
     tenancy shape.** Every row has exactly one owning org.
   - Need another org to see or act on this row? Do NOT add a counterparty
     column, a recipients table, or a `published` flag - those give a row
     more than one owner and are exactly what ADR 48 removed. Instead the
     owner's aggregate holds who-it-is-shared-with as DATA, publishes an
     event, and a handler materializes an `IOrgScoped` row in EACH other
     org's tenant (`PublishForOrgAsync` to that org, so it lands under their
     RLS session). `org_directory` is the worked example. Read ADR 48 and
     `docs/cross-tenant-sharing.md` before modeling any cross-org feature:
     the recipe covers targeted push (`FanOutAsync`) versus open pull (a
     platform-global projection), and maps every removed shape to its owned
     equivalent.
   - Workflow authority ("may a vendor award?") is a command on the object
     you own, never a `WITH CHECK` clause.
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
