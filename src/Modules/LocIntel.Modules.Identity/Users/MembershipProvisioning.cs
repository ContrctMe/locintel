using LocIntel.Contracts;
using LocIntel.Modules.Identity.Access;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// A role INTENT recorded when an invitation is sent (ADR 6: grants are
/// internal - the provider carries the invitation, we carry what the invitee
/// becomes). Consumed on the invitee's first login into the org.
/// </summary>
public sealed class InvitedRole : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Email { get; init; }
    public required Guid RoleId { get; init; }
    public required string InvitationExternalId { get; init; }
    public required Guid InvitedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Shared bootstrap: membership + role wiring used by JIT login, founder provisioning, and invite acceptance.</summary>
public static class MembershipBootstrap
{
    /// <summary>
    /// Creates the membership and assigns roles: a recorded invited role wins;
    /// otherwise the org's FIRST member becomes Owner (*:*).
    /// </summary>
    public static async Task<Membership> EnsureMembershipAsync(
        IdentityDbContext db,
        AppUser user,
        OrgId orgId,
        CancellationToken ct
    )
    {
        var membership = await db.Memberships.FirstOrDefaultAsync(
            m => m.UserId == user.Id && m.OrgId == orgId,
            ct
        );
        if (membership is not null)
            return membership;

        membership = Membership.Create(user.Id, orgId);
        db.Memberships.Add(membership);

        var invited = await db.Set<InvitedRole>()
            .FirstOrDefaultAsync(i => i.OrgId == orgId && i.Email == user.Email, ct);
        if (invited is not null)
        {
            db.MembershipRoles.Add(
                new MembershipRole
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = orgId,
                    MembershipId = membership.Id,
                    RoleId = invited.RoleId,
                    ScopePath = null,
                }
            );
            db.Set<InvitedRole>().Remove(invited);
        }
        else if (!await db.Roles.AnyAsync(r => r.OrgId == orgId, ct))
        {
            var owner = Role.Create(orgId, "Owner");
            db.Roles.Add(owner);
            db.RoleGrants.Add(
                new RoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = orgId,
                    RoleId = owner.Id,
                    Domain = "*",
                    Action = "*",
                }
            );
            db.MembershipRoles.Add(
                new MembershipRole
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = orgId,
                    MembershipId = membership.Id,
                    RoleId = owner.Id,
                    ScopePath = null,
                }
            );
        }
        return membership;
    }
}

public static class ProvisionFounderMembershipHandler
{
    [Transactional(typeof(IdentityDbContext))]
    public static async Task Handle(
        ProvisionFounderMembership message,
        Envelope envelope,
        ITenantContext tenant,
        IdentityDbContext db,
        IAuthProvider provider,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"ProvisionFounderMembership arrived with no tenant (TenantId='{envelope.TenantId}')"
            );

        var user = await db.Users.FirstAsync(u => u.Id == message.UserId, ct);
        await MembershipBootstrap.EnsureMembershipAsync(db, user, org, ct);
        await db.SaveChangesAsync(ct);

        // provider-side membership so future AuthKit logins carry the org
        var directoryEntry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org, ct);
        if (
            provider is IOrganizationDirectory directory
            && directoryEntry?.ExternalId is { } externalOrgId
            && user.Provider == provider.Name
        )
        {
            await directory.AddMemberAsync(externalOrgId, user.Subject, ct);
        }
    }
}
