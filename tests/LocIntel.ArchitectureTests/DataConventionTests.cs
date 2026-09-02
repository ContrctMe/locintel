using System.Reflection;
using NetArchTest.Rules;
using LocIntel.Platform.Data;

namespace LocIntel.ArchitectureTests;

public class DataConventionTests
{
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

    private static IEnumerable<Type> AllTypes() =>
        new[]
        {
            typeof(Modules.Tenancy.TenancyModule).Assembly,
            typeof(Platform.Kernel.OrgId).Assembly,
        }.SelectMany(a => a.GetTypes());
}
