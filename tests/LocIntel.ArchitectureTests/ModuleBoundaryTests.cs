using System.Reflection;
using NetArchTest.Rules;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// The compiler-adjacent enforcement of ADR 17: modules communicate only
/// through LocIntel.Contracts and Wolverine messages. These tests are fast
/// on purpose - a Stop hook and CI both run them.
/// </summary>
public class ModuleBoundaryTests
{
    // derived from the one module catalog (composition root), so this list
    // cannot drift from the modules the host actually wires
    private static readonly Assembly[] ModuleAssemblies =
    [
        .. LocIntel.Api.ModuleCatalog.All.Select(m => m.DbContextType.Assembly).Distinct(),
    ];

    private const string ModulePrefix = "LocIntel.Modules.";

    public static IEnumerable<object[]> Modules() =>
        ModuleAssemblies.Select(a => new object[] { a });

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_other_modules_internals(Assembly module)
    {
        var self = module.GetName().Name!;
        var otherModules = ModuleAssemblies
            .Select(a => a.GetName().Name!)
            .Where(n => n != self)
            .ToArray();
        if (otherModules.Length == 0)
            return; // single module so far

        var result = Types
            .InAssembly(module)
            .ShouldNot()
            .HaveDependencyOnAny(otherModules)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{self} references another module's internals: "
                + string.Join(", ", result.FailingTypeNames ?? [])
        );
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_does_not_reference_the_host(Assembly module)
    {
        var result = Types
            .InAssembly(module)
            .ShouldNot()
            .HaveDependencyOn("LocIntel.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, $"{module.GetName().Name} must not depend on the host.");
    }

    /// <summary>
    /// The contract-consumption ladder. Modules may implement any contract and
    /// consume contracts implemented BELOW them; consuming upward creates the
    /// extraction-blocking cycles the assembly-reference tests cannot see:
    ///   Tenancy (base: org/site master data - consumes no module's contracts)
    ///   Identity (above Tenancy - reads org data ONLY via its event-fed
    ///             org_directory read model, never IOrganizationLookup)
    ///   Entitlements (may consume IOrganizationLookup and the probes)
    ///   Storage, Spatial (above those - own bytes and geometry; a lower
    ///             module reaches them through a Platform port, never directly)
    ///   Reporting (top - consumes site, file, overlay and requester contracts
    ///             implemented below it, and publishes none of its own)
    /// Platform ports (IScopeResolver, IEntitlements) are exempt: the host
    /// wires them, and their hub-shaped runtime coupling is a documented
    /// decision (ADR 37), not an accident.
    /// </summary>
    [Fact]
    public void Identity_does_not_consume_tenancy_contracts()
    {
        var result = Types
            .InAssembly(typeof(Modules.Identity.IdentityModule).Assembly)
            .ShouldNot()
            .HaveDependencyOn("LocIntel.Contracts.IOrganizationLookup")
            .GetResult();
        Assert.True(
            result.IsSuccessful,
            "Identity must use its org_directory read model, not Tenancy's lookup: "
                + string.Join(", ", result.FailingTypeNames ?? [])
        );
    }

    /// <summary>
    /// Reporting is the ladder's top rung, so nothing but the host may reference
    /// it. The theory above already forbids module-to-module references, but the
    /// direction is what matters here and it is worth failing by name: a fork
    /// that wires Storage or Tenancy to a report type has made Reporting
    /// impossible to extract, and would otherwise learn that from a generic
    /// message about "another module's internals".
    /// </summary>
    [Fact]
    public void Nothing_below_reporting_consumes_it()
    {
        var reporting = typeof(Modules.Reporting.ReportRegistry).Assembly.GetName().Name!;
        foreach (var module in ModuleAssemblies.Where(a => a.GetName().Name != reporting))
        {
            var result = Types
                .InAssembly(module)
                .ShouldNot()
                .HaveDependencyOn(reporting)
                .GetResult();
            Assert.True(
                result.IsSuccessful,
                $"{module.GetName().Name} consumes Reporting, which sits above it: "
                    + string.Join(", ", result.FailingTypeNames ?? [])
            );
        }
    }

    // every adapter the host wires (the scan used to cover WorkOS alone)
    public static IEnumerable<object[]> Integrations() =>
        typeof(LocIntel.Api.ModuleCatalog)
            .Assembly.GetReferencedAssemblies()
            .Where(a => a.Name!.StartsWith("LocIntel.Integrations.", StringComparison.Ordinal))
            .Select(a => new object[] { Assembly.Load(a) });

    [Fact]
    public void Every_integration_project_in_the_solution_is_wired_into_the_host()
    {
        // an adapter project the host does not reference cannot be selected by
        // configuration - a seam that exists in the tree but not in the image
        var root = RepositoryRoot();
        var inSolution = File.ReadAllLines(Path.Combine(root, "LocIntel.slnx"))
            .Where(l =>
                l.Contains("src/Integrations/LocIntel.Integrations.", StringComparison.Ordinal)
            )
            .Select(l => l.Split('/')[2])
            .Order()
            .ToArray();
        var wired = Integrations().Select(a => ((Assembly)a[0]).GetName().Name!).Order().ToArray();
        Assert.NotEmpty(inSolution);
        Assert.Equal(inSolution, wired);
    }

    [Theory]
    [MemberData(nameof(Integrations))]
    public void Integrations_reference_only_platform(Assembly integration)
    {
        // ADR 14: adapters implement Platform ports; they never reach into modules.
        var result = Types
            .InAssembly(integration)
            .ShouldNot()
            .HaveDependencyOnAny(ModulePrefix + "*", "LocIntel.Api")
            .GetResult();
        Assert.True(result.IsSuccessful, "Integrations depend on Platform ports only.");
    }

    [Fact]
    public void Platform_does_not_reference_any_module()
    {
        var result = Types
            .InAssembly(typeof(Platform.Kernel.OrgId).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ModulePrefix)
            .GetResult();
        Assert.True(
            result.IsSuccessful,
            "Platform is the shared kernel; it must not know about modules."
        );
    }

    [Fact]
    public void Contracts_reference_nothing_but_platform_kernel()
    {
        var result = Types
            .InAssembly(typeof(Contracts.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ModulePrefix + "*",
                "LocIntel.Api",
                "Microsoft.EntityFrameworkCore"
            )
            .GetResult();
        Assert.True(result.IsSuccessful, "Contracts carry DTOs and integration events only.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
