using System.Reflection;
using LocIntel.Platform.Data;
using NetArchTest.Rules;

namespace LocIntel.ArchitectureTests;

public class DataConventionTests
{
    [Fact]
    public void The_scan_covers_every_module()
    {
        // the guard is only as wide as its input: eight contexts, one per catalog entry
        var contexts = AllTypes()
            .Where(t => typeof(ModuleDbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .Count();
        Assert.Equal(LocIntel.Api.ModuleCatalog.AllWithPlatform.Count(), contexts);
    }

    [Fact]
    public void Every_DbContext_derives_from_ModuleDbContext()
    {
        // ADR 17: no module gets a bare DbContext - the base class carries the
        // schema, tenant filter, and soft-delete filter conventions.
        var offenders = AllTypes()
            .Where(t =>
                typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(t)
                && t is { IsAbstract: false }
                && !typeof(ModuleDbContext).IsAssignableFrom(t)
            )
            .Select(t => t.FullName)
            .ToList();
        Assert.True(
            offenders.Count == 0,
            "DbContexts must derive from ModuleDbContext: " + string.Join(", ", offenders)
        );
    }

    // every catalogued module plus Platform - the scan used to cover Tenancy
    // and Platform only, so a bare DbContext in any other module passed
    private static IEnumerable<Type> AllTypes() =>
        LocIntel
            .Api.ModuleCatalog.AllWithPlatform.Select(m => m.DbContextType.Assembly)
            .Distinct()
            .SelectMany(a => a.GetTypes());
}
