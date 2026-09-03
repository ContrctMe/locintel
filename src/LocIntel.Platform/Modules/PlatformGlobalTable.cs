namespace LocIntel.Platform.Modules;

/// <summary>
/// A table in a module's schema that carries an org column but is
/// deliberately NOT under row-level security: it is resolved BEFORE any
/// tenant context exists (a credential lookup, membership resolution, a
/// pre-tenant read model, a public projection) and protected by explicit
/// query filters instead. Declaring one is a security decision, so it is
/// made where the module is declared - on its catalog entry, with the
/// reason beside it - and RlsCoverageTests reads it from there. It used to
/// be an allow-list inside that test, which put a design decision in an
/// upstream test file every fork had to edit.
/// </summary>
/// <param name="Table">The table name, without schema.</param>
/// <param name="Reason">Why it is safe without RLS: what resolves it, and what filters it.</param>
public sealed record PlatformGlobalTable(string Table, string Reason);
