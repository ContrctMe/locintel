using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using Wolverine.Attributes;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Identity's slice of the offboarding export: who could get in and what they
/// could do. Emails belong to the org's own members - this is their admin
/// taking the org's data, not a cross-tenant disclosure.
/// </summary>
public sealed class IdentityExporter(IdentityDbContext db) : IOrgDataExporter
{
    public string Section => "identity";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var members = await db
            .Memberships.IgnoreQueryFilters()
            .Where(m => m.OrgId == org)
            .Join(
                db.Users,
                m => m.UserId,
                u => u.Id,
                (m, u) =>
                    new
                    {
                        u.Email,
                        joinedAt = m.CreatedAt,
                        roles = db
                            .MembershipRoles.IgnoreQueryFilters()
                            .Where(mr => mr.MembershipId == m.Id)
                            .Join(db.Roles, mr => mr.RoleId, r => r.Id, (mr, r) => r.Name)
                            .ToList(),
                    }
            )
            .ToListAsync(ct);
        var roles = await db
            .Roles.IgnoreQueryFilters()
            .Where(r => r.OrgId == org)
            .Select(r => new
            {
                r.Name,
                grants = db
                    .RoleGrants.IgnoreQueryFilters()
                    .Where(g => g.RoleId == r.Id)
                    .Select(g => new { g.Domain, g.Action })
                    .ToList(),
            })
            .ToListAsync(ct);
        var exceptions = await db
            .GrantExceptions.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .Select(e => new
            {
                e.Domain,
                e.Action,
                e.Reason,
                e.ExpiresAt,
                e.CreatedAt,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new
            {
                members,
                roles,
                exceptions,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}

/// <summary>
/// The org is gone: access artifacts go with it - memberships, roles, grants,
/// invitation intents, and the directory entry the enforcement middleware
/// reads. Users themselves remain (people outlive any one org), and the
/// provider's org is deleted so WorkOS agrees with us about what exists.
/// </summary>
public static class OrganizationDeletedHandler
{
    [Transactional]
    public static async Task Handle(
        OrganizationDeleted evt,
        IdentityDbContext db,
        IAuthProvider provider,
        CancellationToken ct
    )
    {
        var org = evt.OrgId;
        await db.Contacts.IgnoreQueryFilters().Where(c => c.OrgId == org).ExecuteDeleteAsync(ct);
        await db.ApiKeys.IgnoreQueryFilters().Where(k => k.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Set<InvitedRole>()
            .IgnoreQueryFilters()
            .Where(i => i.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db
            .GrantExceptions.IgnoreQueryFilters()
            .Where(e => e.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db
            .MembershipRoles.IgnoreQueryFilters()
            .Where(mr => mr.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db.RoleGrants.IgnoreQueryFilters().Where(g => g.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Roles.IgnoreQueryFilters().Where(r => r.OrgId == org).ExecuteDeleteAsync(ct);
        await db.Memberships.IgnoreQueryFilters().Where(m => m.OrgId == org).ExecuteDeleteAsync(ct);
        await db
            .OrgDirectory.IgnoreQueryFilters()
            .Where(d => d.OrgId == org)
            .ExecuteDeleteAsync(ct);

        if (provider is IOrganizationDirectory directory && evt.ExternalId is { } externalId)
            await directory.DeleteOrganizationAsync(externalId, ct);
    }
}
