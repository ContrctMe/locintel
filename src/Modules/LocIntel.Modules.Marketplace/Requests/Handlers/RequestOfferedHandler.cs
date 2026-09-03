using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Requests.Handlers;

/// <summary>Runs as the VENDOR: materializes (or refreshes) its own assignment row for the offered request.</summary>
public static class RequestOfferedHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        RequestOffered message,
        Envelope envelope,
        ITenantContext tenant,
        MarketplaceDbContext db,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"RequestOffered arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        await db.TakeAsync(org, message.Request.RequestId, ct);
        var s = message.Request;
        var row = await db.Assignments.FirstOrDefaultAsync(a => a.RequestId == s.RequestId, ct);
        if (row is null)
        {
            row = new VendorAssignment
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                RequestId = s.RequestId,
                RequesterOrgId = s.RequesterOrgId,
                RequesterName = s.RequesterName,
                Participation = message.Participation,
            };
            db.Assignments.Add(row);
        }
        row.Apply(s);
        if (s.VendorOrgId is null)
            row.Participation = message.Participation;
        await db.SaveChangesAsync(ct);
    }
}
