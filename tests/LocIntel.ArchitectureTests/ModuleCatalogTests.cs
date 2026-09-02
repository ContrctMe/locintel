using System.Reflection;
using LocIntel.Api;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// The catalog is only trustworthy if it is COMPLETE. A fork reported the
/// template's hand-maintained module lists rotting - Checklists was absent
/// from the migration round-trip and the org data export, silently. Now the
/// lists derive from one catalog, and this test asserts the catalog covers
/// every module assembly the host references, so a half-registered module
/// fails the build instead of skipping migrations, grants, and exports.
/// </summary>
public class ModuleCatalogTests
{
    [Fact]
    public void Every_module_assembly_appears_in_the_catalog()
    {
        var referenced = typeof(Program)
            .Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("LocIntel.Modules.", StringComparison.Ordinal))
            .ToHashSet();

        var registered = ModuleCatalog
            .All.Select(m => m.DbContextType.Assembly.GetName().Name!)
            .ToHashSet();

        var missing = referenced.Except(registered).Order().ToArray();
        Assert.True(
            missing.Length == 0,
            "module assemblies referenced by the host but absent from ModuleCatalog.All "
                + "(they would skip migration, grants, RLS coverage, round-trips and export): "
                + string.Join(", ", missing)
        );
    }

    [Fact]
    public void Every_module_contributes_an_org_data_export_section()
    {
        // the Checklists bug in the flesh: a module with tenant data but no
        // IOrgDataExporter drops out of the org export silently, which is
        // data loss on offboarding and a GDPR-portability hole
        var missing = ModuleCatalog
            .All.Where(module =>
                !module
                    .DbContextType.Assembly.GetTypes()
                    .Any(t =>
                        t is { IsAbstract: false, IsInterface: false }
                        && typeof(LocIntel.Contracts.IOrgDataExporter).IsAssignableFrom(t)
                    )
            )
            .Select(m => m.Name)
            .Order()
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "modules with no IOrgDataExporter - their rows would never leave with "
                + "an org's data export: "
                + string.Join(", ", missing)
        );
    }

    [Fact]
    public void Catalog_names_and_schemas_are_unique_and_lowercase()
    {
        foreach (var module in ModuleCatalog.AllWithPlatform)
        {
            Assert.Equal(module.Name.ToLowerInvariant(), module.Name);
            Assert.Equal(module.Schema.ToLowerInvariant(), module.Schema);
        }
        Assert.Equal(
            ModuleCatalog.AllWithPlatform.Count(),
            ModuleCatalog.AllWithPlatform.Select(m => m.Schema).Distinct().Count()
        );
    }
}
