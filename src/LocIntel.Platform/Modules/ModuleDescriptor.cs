namespace LocIntel.Platform.Modules;

/// <summary>
/// One module's identity for the things that must enumerate ALL modules:
/// migration, app-role grants, RLS coverage, migration round-trips, the org
/// data export, and the test fixture.
///
/// It exists because those lists used to be hand-maintained in six files and
/// rotted in silence - a fork found Checklists missing from two of them,
/// which meant its migrations were never round-tripped and its rows never
/// left with an org's data export. A module that forgets to register here
/// fails an architecture test, so the rot is no longer expressible.
/// </summary>
/// <param name="Name">Lowercase module name, matching its assembly suffix.</param>
/// <param name="Schema">Its Postgres schema - the unit of grants and RLS coverage.</param>
/// <param name="DbContextType">Its DbContext, the migration history owner.</param>
/// <param name="PlatformGlobal">
/// Tables in this schema that carry an org column yet are deliberately not
/// under RLS, each with its reason - see <see cref="PlatformGlobalTable"/>.
/// Empty for almost every module.
/// </param>
public sealed record ModuleDescriptor(string Name, string Schema, Type DbContextType)
{
    public IReadOnlyList<PlatformGlobalTable> PlatformGlobal { get; init; } = [];
}
