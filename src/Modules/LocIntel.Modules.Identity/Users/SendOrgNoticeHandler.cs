using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Notifications;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Delivers an org notice to every member holding org:manage - grant
/// evaluation per member, not a hardcoded "owner" role, so custom role
/// setups get the right recipients. Sends ride the same transport (and
/// bounce suppression) as everything else.
/// </summary>
public static class SendOrgNoticeHandler
{
    [Transactional(typeof(IdentityDbContext))]
    public static async Task Handle(
        SendOrgNotice message,
        Envelope envelope,
        ITenantContext tenant,
        IdentityDbContext db,
        IScopeResolver scopes,
        INotificationTransport transport,
        ISmsTransport sms,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SendOrgNotice arrived with no tenant (TenantId='{envelope.TenantId}')"
            );
        var orgName = await db
            .OrgDirectory.Where(d => d.OrgId == org)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct);
        var members = await (
            from membership in db.Memberships
            where membership.OrgId == org
            join user in db.Users on membership.UserId equals user.Id
            select new
            {
                user.Id,
                user.Email,
                user.Name,
            }
        ).ToListAsync(ct);

        foreach (var member in members)
        {
            var principal = new Principal.User(member.Id, member.Email, member.Name, org);
            if (!await scopes.CanAsync(principal, Capabilities.OrgManage, ct))
                continue;
            await transport.SendAsync(
                EmailTemplate.Render(
                    member.Email,
                    message.Subject,
                    orgName ?? "Your organization",
                    message.BodyLines,
                    footer: $"You receive this because you manage {orgName} on this platform."
                ),
                ct
            );
        }

        // SMS: members who opted in to this kind AND hold its capability;
        // never managers-by-default, never without a phone on file
        if (message.Sms is { Length: > 0 } text && KindCapability(message.Kind) is { } capability)
        {
            var prefs = await db
                .NotificationPreferences.Where(p => p.Phone != null)
                .ToListAsync(ct);
            var body = text.Length > 300 ? text[..297] + "..." : text;
            foreach (var pref in prefs.Where(p => p.WantsSms(message.Kind)))
            {
                var member = members.FirstOrDefault(m => m.Id == pref.UserId);
                if (member is null)
                    continue;
                var principal = new Principal.User(member.Id, member.Email, member.Name, org);
                if (!await scopes.CanAsync(principal, capability, ct))
                    continue;
                await sms.SendAsync(
                    new SmsMessage(pref.Phone!, $"{orgName ?? "LocIntel"}: {body}"),
                    ct
                );
            }
        }
    }

    /// <summary>Which capability a channel kind implies - the SMS goes to people who could act on it in the console.</summary>
    private static string? KindCapability(string kind) =>
        kind switch
        {
            "alerts" => Capabilities.AlertsRead,
            "marketplace" => Capabilities.MarketplaceRead,
            "network" => Capabilities.NetworkRead,
            _ => null,
        };
}
