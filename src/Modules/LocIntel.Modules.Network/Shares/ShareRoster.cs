using System.Text.Json;
using LocIntel.Modules.Network.Data;
using LocIntel.Modules.Network.Shares.Messages;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace LocIntel.Modules.Network.Shares;

/// <summary>
/// The owner's push (ADR 48): the roster and share projection go to every
/// member as data, including the owner's own access row, so every org reads
/// the share from a row it owns.
/// </summary>
public static class ShareRoster
{
    public static string Json(IEnumerable<ShareMember> members) =>
        JsonSerializer.Serialize(members.OrderBy(m => m.CreatedAt).Select(m => m.Entry()));

    /// <summary>Recompute from the owner's roster, refresh the owner's own access row, fan out to the rest. Call after SaveChanges.</summary>
    public static async Task PublishAsync(
        NetworkDbContext db,
        IMessageBus bus,
        Share share,
        CancellationToken ct
    )
    {
        var members = await db.Members.Where(m => m.ShareId == share.Id).ToListAsync(ct);
        var roster = Json(members);
        var own = await db.Access.FirstOrDefaultAsync(a => a.ShareId == share.Id, ct);
        if (own is not null)
        {
            own.ShareName = share.Name;
            own.Description = share.Description;
            own.ShareStatus = share.Status;
            own.RosterJson = roster;
            await db.SaveChangesAsync(ct);
        }
        var peers = members
            .Where(m => m.MemberOrgId != share.OrgId && m.Status != MembershipStatus.Removed)
            .Select(m => m.MemberOrgId)
            .ToList();
        // the owner never handles its own roster message, so it re-offers here what
        // a member's handler re-offers on receipt: its active bulletins, to the active
        // peers (copies land once - a newly joined member gets what it missed)
        var active = members
            .Where(m => m.Status == MembershipStatus.Active && m.MemberOrgId != share.OrgId)
            .Select(m => m.MemberOrgId)
            .ToList();
        var now = DateTimeOffset.UtcNow;
        foreach (
            var bulletin in await db
                .Bulletins.Where(b =>
                    b.ShareId == share.Id && b.WithdrawnAt == null && b.ExpiresAt > now
                )
                .ToListAsync(ct)
        )
            await bus.FanOutAsync(active, bulletin.Offered(share.Name), bulletin.Id);
        await bus.FanOutAsync(
            peers,
            new ShareRosterChanged(
                share.Id,
                share.Name,
                share.Description,
                share.OwnerName,
                share.Status,
                roster
            ),
            share.Id
        );
    }

    /// <summary>Active members other than the caller, from the caller's own roster snapshot.</summary>
    public static IEnumerable<OrgId> ActivePeers(ShareAccess access) =>
        access
            .Roster()
            .Where(e => e.Status == MembershipStatus.Active && e.OrgId != access.OrgId.Value)
            .Select(e => new OrgId(e.OrgId));
}
