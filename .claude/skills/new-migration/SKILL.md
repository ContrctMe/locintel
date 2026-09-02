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
3. **RLS checklist - every new tenant-scoped table:**
   - `ALTER TABLE <schema>.<table> ENABLE ROW LEVEL SECURITY;`
   - `ALTER TABLE <schema>.<table> FORCE ROW LEVEL SECURITY;` (applies to owner too)
   - Tenant policy: `USING (org_id = current_setting('app.org_id')::uuid)`
   - Raw SQL goes in the migration via `migrationBuilder.Sql(...)`.
   - Platform-global tables (no org_id) are the exception - say so explicitly in
     a migration comment so the CI coverage assertion can allowlist them.
4. **Column checklist:**
   - UUIDv7 keys, never sequences/identity for entity ids (ADR 35)
   - timestamptz for instants; document which temporal kind each column is (ADR 26)
   - soft-delete tier entities: `deleted_at` + partial unique indexes where needed (ADR 25)
5. **`Down()` is maintained, not decorative** (ADR 38): `MigrationRoundTripTests`
   applies every migration, reverts to 0, and applies again — a `Down()` that
   does not truly reverse `Up()` fails the build. Never drop the module's
   schema in `Down()`; it holds the migration history table.
6. Review generated SQL with `dotnet ef migrations script` before considering it done.
7. Run the module's tests plus the tenant-isolation suite (the round-trip
   test runs with the integration suite).
