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
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(Modules.Tenancy.TenancyModule).Assembly,
        typeof(Modules.Identity.IdentityModule).Assembly,
        typeof(Modules.Entitlements.EntitlementsModule).Assembly,
        typeof(Modules.Audit.AuditModule).Assembly,
        typeof(Modules.Storage.StorageModule).Assembly,
        typeof(Modules.Ingest.IngestModule).Assembly,
        typeof(Modules.Checklists.ChecklistsModule).Assembly,
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
    ///   Entitlements (top - may consume IOrganizationLookup and the probes)
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

    [Fact]
    public void Integrations_reference_only_platform()
    {
        // ADR 14: adapters implement Platform ports; they never reach into modules.
        var result = Types
            .InAssembly(typeof(Integrations.WorkOS.WorkOSAuthProvider).Assembly)
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
            .HaveDependencyOnAny(ModulePrefix + "*", "LocIntel.Api", "Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful, "Contracts carry DTOs and integration events only.");
    }
}
