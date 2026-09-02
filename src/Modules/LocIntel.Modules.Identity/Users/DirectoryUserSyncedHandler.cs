using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Auth;
using LocIntel.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Applies directory-sync events (ADR 41). Upsert ensures user + membership
/// (roles stay internal - an invited role wins, otherwise the admin assigns
/// them in the role editor). Removal is the point of directory sync: the
/// membership goes (tier 3) and ALL the user's sessions are revoked - a
/// multi-org user signs back into their other orgs; security wins. The IdP's
/// word is final, so removal skips the last-manager guard on purpose.
/// </summary>
public static class DirectoryUserSyncedHandler
{
    [Transactional(typeof(IdentityDbContext))]
    public static async Task Handle(
        DirectoryUserSynced message,
        Envelope envelope,
        ITenantContext tenant,
        IdentityDbContext db,
        IAuthProvider provider,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"DirectoryUserSynced arrived with no tenant (TenantId='{envelope.TenantId}')"
            );

        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Provider == provider.Name && u.Email == message.Email,
            ct
        );

        if (message.Kind == DirectorySyncKind.UserRemoved)
        {
            if (user is null)
                return; // never logged in and never provisioned: nothing to revoke
            var membership = await db.Memberships.FirstOrDefaultAsync(
                m => m.UserId == user.Id && m.OrgId == org,
                ct
            );
            if (membership is null)
                return;
            await db
                .MembershipRoles.Where(r => r.MembershipId == membership.Id)
                .ExecuteDeleteAsync(ct);
            db.Memberships.Remove(membership);
            await db.Sessions.Where(s => s.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
            await PublishAuditAsync(bus, org, "member.deprovisioned", message.Email);
            return;
        }

        if (user is null)
        {
            // pre-provision at the provider so the id we record here is the
            // one their first AuthKit login will assert as the subject
            var externalId = provider is IUserProvisioning provisioning
                ? await provisioning.EnsureUserAsync(message.Email, ct)
                : $"directory_{message.Email}";
            user =
                await db.Users.FirstOrDefaultAsync(
                    u => u.Provider == provider.Name && u.Subject == externalId,
                    ct
                ) ?? AppUser.Create(provider.Name, externalId, message.Email, message.Name);
            if (db.Entry(user).State == EntityState.Detached)
                db.Users.Add(user);
        }

        var existed = await db.Memberships.AnyAsync(m => m.UserId == user.Id && m.OrgId == org, ct);
        await MembershipBootstrap.EnsureMembershipAsync(db, user, org, ct);
        await db.SaveChangesAsync(ct);
        if (existed)
            return; // re-delivery or profile update: membership already stands

        // provider-side membership so future AuthKit logins carry the org
        // (same wiring as founder provisioning)
        var directoryEntry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org, ct);
        if (
            provider is IOrganizationDirectory directory
            && directoryEntry?.ExternalId is { } externalOrgId
        )
            await directory.AddMemberAsync(externalOrgId, user.Subject, ct);
        await PublishAuditAsync(bus, org, "member.provisioned", message.Email);
    }

    private static async Task PublishAuditAsync(
        IMessageBus bus,
        OrgId org,
        string action,
        string email
    ) =>
        await bus.PublishAsync(
            new RecordDomainAudit(
                action,
                System.Text.Json.JsonSerializer.Serialize(new { email, source = "directory" })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers = { ["locintel-actor-tier"] = "system" },
            }
        );
}
