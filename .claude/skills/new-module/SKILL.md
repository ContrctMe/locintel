---
name: new-module
description: Scaffold a new vertical-slice module with its own schema, DbContext, migration history, Wolverine registration, and test fixtures. Use whenever adding a module, domain slice, or bounded context to the backend.
---

# New module

Every module is a vertical slice owning its own Postgres schema and DbContext
(ADR 17). Never hand-roll one - missing a step here is the main way module
boundaries erode.

## The generator wires itself

`python3 tools/new-module.py <Name>` now APPLIES the wiring rather than
printing it: solution entry, the API project reference, the Program.cs using +
registration + Wolverine discovery, and the `ModuleCatalog` entry - which is
the single line that makes migrations, app-role grants, RLS coverage, the
migration round-trip and the test fixture pick the module up. Any edit it
cannot place is reported with the exact line to add by hand.

It also scaffolds the module's `IOrgDataExporter`, because an architecture
test requires every module to contribute an org-export section (a module
without one drops out of offboarding silently). Fill in its projection.

Left to you: the first migration, the exporter body, and - if the module owns
tenant rows - a `PurgeOrg<Name>` message published from `OrgPurgeFanOut`.

## Run the generator first

```bash
python3 tools/new-module.py <Name>
```

It scaffolds the project (csproj, ModuleDbContext, design-time factory,
registration extension with both interceptors) and prints the 7-line wiring
checklist. The steps below are what the generator + checklist cover - verify
rather than re-do

1. **Project layout**: `src/Modules/LocIntel.Modules.<Name>/` with feature folders (one folder
   per use case), not layer folders. Handlers are Wolverine handlers.
2. **Schema + DbContext**: new `<ModuleName>DbContext` with
   `HasDefaultSchema("<module_name>")` and its own `__EFMigrationsHistory` in
   that schema. Register the connection through the region-resolved context
   factory - never a raw connection string (ADR 35).
3. **First migration**: create the schema; every tenant-scoped table gets an RLS
   policy in the same migration (use the new-migration skill).
4. **Wolverine registration**: register the module's handlers and any
   integration message subscriptions. Cross-module communication is messages +
   outbox only - no project reference to another module's internals.
5. **Contracts**: anything other modules may consume goes in
   `src/LocIntel.Contracts/<Name>/` (DTOs and integration events only).
6. **Architecture tests**: add the module to the boundary rules (may not
   reference other modules' internals; must only expose contracts).
7. **Test fixtures**: per-module fixture that provisions the schema, applies
   migrations, and sets tenant context. Add the module's endpoints to the
   tenant-isolation golden suite (replay as tenant B against tenant A's ids,
   assert 404).
8. **Entity checklist**, for every new entity:
   - deletion tier declared (ADR 25)
   - temporal columns typed as one of the four kinds (ADR 26/27)
   - UUIDv7 keys (ADR 35)
   - fact tables stamp hierarchy path + business date (ADR 2/26)

## Wolverine traps that only fail at runtime

- `[Transactional(typeof(X))]` must name a context the endpoint's dependency
  chain actually supplies. Naming an absent one fails at HOST STARTUP, which
  looks like every test in every class failing at 1ms with no message.
  `TransactionalAttributeTests` catches it at build time instead.
- An endpoint returning `Task<IResult>` with no declared 200 type generates a
  client that accepts anything. Declare
  `[ProducesResponseType(typeof(T), StatusCodes.Status200OK)]`, and prefer
  echoing a typed state record over returning 204 - the typed-response ratchet
  fails newcomers.
- Register services by TYPE (`AddScoped<IFoo, Foo>()`), never lambda factories.
- Registering a DERIVED interface does not satisfy a BASE one: if something
  resolves the base port, register it explicitly.
