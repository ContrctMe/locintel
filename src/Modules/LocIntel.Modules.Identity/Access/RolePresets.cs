using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Identity.Access;

/// <summary>
/// The roles every org is born with (flow review, 2026-09): an invite offers
/// a real choice on day one instead of "Owner" as the only row, and nobody
/// has to learn capability codes to give a store manager a checklist.
/// Bundles are org data from the moment they exist - owners rename, reshape
/// or delete them; only Owner is required to stay a role that manages roles.
/// </summary>
public static class RolePresets
{
    public sealed record Preset(string Name, IReadOnlyList<(string Domain, string Action)> Grants);

    public const string OwnerName = "Owner";

    private static (string Domain, string Action) Grant(string capability)
    {
        var parts = capability.Split(':');
        return (parts[0], parts[1]);
    }

    public static readonly IReadOnlyList<Preset> All =
    [
        new(OwnerName, [("*", "*")]),
        // everything an owner does except the org's own settings and plan
        new(
            "Admin",
            [
                Grant(Capabilities.SitesRead),
                Grant(Capabilities.SitesManage),
                Grant(Capabilities.HierarchyManage),
                Grant(Capabilities.FilesRead),
                Grant(Capabilities.FilesManage),
                Grant(Capabilities.IngestManage),
                Grant(Capabilities.ReportsGenerate),
                Grant(Capabilities.ChecklistsManage),
                Grant(Capabilities.ChecklistsComplete),
                Grant(Capabilities.OverlaysRead),
                Grant(Capabilities.OverlaysManage),
                Grant(Capabilities.AuditRead),
                Grant(Capabilities.AuditManage),
                Grant(Capabilities.RolesManage),
            ]
        ),
        // runs the sites in a subtree: their details, hours, lists and shapes
        new(
            "Regional manager",
            [
                Grant(Capabilities.SitesRead),
                Grant(Capabilities.SitesManage),
                Grant(Capabilities.FilesRead),
                Grant(Capabilities.FilesManage),
                Grant(Capabilities.ChecklistsManage),
                Grant(Capabilities.ChecklistsComplete),
                Grant(Capabilities.OverlaysRead),
                Grant(Capabilities.AuditRead),
            ]
        ),
        // opens the store: today's lists, and what the site is
        new(
            "Site manager",
            [
                Grant(Capabilities.SitesRead),
                Grant(Capabilities.ChecklistsComplete),
                Grant(Capabilities.FilesRead),
                Grant(Capabilities.OverlaysRead),
            ]
        ),
        new(
            "Viewer",
            [
                Grant(Capabilities.SitesRead),
                Grant(Capabilities.FilesRead),
                Grant(Capabilities.OverlaysRead),
            ]
        ),
    ];

    /// <summary>Materialize every preset for an org; returns the Owner role for the founder.</summary>
    public static Role Seed(OrgId orgId, Action<Role> addRole, Action<RoleGrant> addGrant)
    {
        Role? owner = null;
        foreach (var preset in All)
        {
            var role = Role.Create(orgId, preset.Name);
            addRole(role);
            foreach (var (domain, action) in preset.Grants)
                addGrant(
                    new RoleGrant
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = orgId,
                        RoleId = role.Id,
                        Domain = domain,
                        Action = action,
                    }
                );
            if (preset.Name == OwnerName)
                owner = role;
        }
        return owner!;
    }
}
