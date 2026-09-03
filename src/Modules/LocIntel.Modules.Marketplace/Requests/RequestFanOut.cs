using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The requester's push (ADR 48, docs/cross-tenant-sharing.md): who holds a
/// copy of a request, and how state and timeline entries reach them. The
/// request id is the correlation on every envelope.
/// </summary>
public static class RequestFanOut
{
    /// <summary>Everyone holding an assignment row: the vendor, plus every broadcast recipient (declined ones too - their row still needs the final state).</summary>
    public static async Task<List<OrgId>> ParticipantsAsync(
        MarketplaceDbContext db,
        ServiceRequest row,
        CancellationToken ct
    )
    {
        var orgs = await db
            .Recipients.Where(x => x.RequestId == row.Id)
            .Select(x => x.VendorOrgId)
            .ToListAsync(ct);
        if (row.VendorOrgId is { } vendor && !orgs.Contains(vendor))
            orgs.Add(vendor);
        return orgs;
    }

    /// <summary>Who a requester's message reaches: the vendor once there is one; before award, every recipient still in.</summary>
    public static async Task<List<OrgId>> CounterpartiesAsync(
        MarketplaceDbContext db,
        ServiceRequest row,
        CancellationToken ct
    ) =>
        row.VendorOrgId is { } vendor
            ? [vendor]
            : await db
                .Recipients.Where(x =>
                    x.RequestId == row.Id && x.Status != RecipientStatus.Declined
                )
                .Select(x => x.VendorOrgId)
                .ToListAsync(ct);

    public static async ValueTask StateAsync(
        MarketplaceDbContext db,
        IMessageBus bus,
        ServiceRequest row,
        CancellationToken ct
    ) =>
        await bus.FanOutAsync(
            await ParticipantsAsync(db, row, ct),
            new RequestStateChanged(row.Snapshot()),
            row.Id
        );

    public static ValueTask EventAsync(
        IMessageBus bus,
        IEnumerable<OrgId> orgs,
        RequestEvent evt
    ) => bus.FanOutAsync(orgs, evt.Appended(), evt.RequestId);
}
