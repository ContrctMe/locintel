using LocIntel.Platform.Modules;

namespace LocIntel.Api;

/// <summary>
/// The single list of modules. The composition root is the one place allowed
/// to know every module (Platform must not reference them - that would invert
/// the dependency the architecture tests enforce), so the catalog lives here
/// and everything that needs to enumerate modules reads it: MigrationRunner
/// migrates and grants from it, and the test suites derive their module lists
/// from it rather than keeping their own copies.
///
/// Adding a module means adding ONE line here. An architecture test asserts
/// every LocIntel.Modules.* assembly appears, so a half-registered module
/// fails the build instead of silently skipping migrations and exports.
/// </summary>
public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDescriptor> All =
    [
        new("tenancy", "tenancy", typeof(LocIntel.Modules.Tenancy.Data.TenancyDbContext)),
        new("identity", "identity", typeof(LocIntel.Modules.Identity.Data.IdentityDbContext))
        {
            // resolved BEFORE any tenant context exists (ADR 7/37): not RLS'd by
            // design. Declaring one here is a security decision, not a shortcut.
            PlatformGlobal =
            [
                new(
                    "api_keys",
                    "looked up by unguessable secret hash, before the principal exists"
                ),
                new("memberships", "filtered by the authenticated user id to find their orgs"),
                new("org_directory", "the pre-tenant org read model (ADR 37)"),
            ],
        },
        new(
            "entitlements",
            "entitlements",
            typeof(LocIntel.Modules.Entitlements.Data.EntitlementsDbContext)
        ),
        new("audit", "audit", typeof(LocIntel.Modules.Audit.Data.AuditDbContext)),
        new("storage", "storage", typeof(LocIntel.Modules.Storage.Data.StorageDbContext)),
        new("ingest", "ingest", typeof(LocIntel.Modules.Ingest.Data.IngestDbContext)),
        new(
            "checklists",
            "checklists",
            typeof(LocIntel.Modules.Checklists.Data.ChecklistsDbContext)
        ),
        new("incidents", "incidents", typeof(LocIntel.Modules.Incidents.Data.IncidentsDbContext)),
        new("entities", "entities", typeof(LocIntel.Modules.Entities.Data.EntitiesDbContext)),
        new("cases", "cases", typeof(LocIntel.Modules.Cases.Data.CasesDbContext)),
        new(
            "marketplace",
            "marketplace",
            typeof(LocIntel.Modules.Marketplace.Data.MarketplaceDbContext)
        )
        {
            // ADR 48 "open: pull": the public projection of published vendors that
            // buyers search and matching reads. Fed by the vendor's own profile
            // through the outbox and carrying nothing a vendor keeps private (no
            // credential numbers), so every tenant may read it.
            PlatformGlobal =
            [
                new(
                    "vendor_directory",
                    "public projection of published vendors (ADR 48 open pull); holds nothing a vendor keeps private"
                ),
            ],
        },
        new("alerts", "alerts", typeof(LocIntel.Modules.Alerts.Data.AlertsDbContext)),
        new("network", "network", typeof(LocIntel.Modules.Network.Data.NetworkDbContext)),
        new("patrols", "patrols", typeof(LocIntel.Modules.Patrols.Data.PatrolsDbContext)),
        new("spatial", "spatial", typeof(LocIntel.Modules.Spatial.Data.SpatialDbContext)),
        new("reporting", "reporting", typeof(LocIntel.Modules.Reporting.Data.ReportingDbContext)),
    ];

    /// <summary>
    /// Platform's own schema rides alongside the modules wherever schemas are
    /// enumerated (grants, RLS coverage, round-trips) but is not a module.
    /// </summary>
    public static readonly ModuleDescriptor Platform = new(
        "platform",
        "platform",
        typeof(LocIntel.Platform.Infra.PlatformDbContext)
    );

    /// <summary>Every migratable context, modules plus Platform.</summary>
    public static IEnumerable<ModuleDescriptor> AllWithPlatform => All.Append(Platform);

    public static IEnumerable<string> Schemas => AllWithPlatform.Select(m => m.Schema);
}
