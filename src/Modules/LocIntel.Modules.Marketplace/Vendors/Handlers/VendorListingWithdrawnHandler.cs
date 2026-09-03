using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Vendors.Messages;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Vendors.Handlers;

public static class VendorListingWithdrawnHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        VendorListingWithdrawn m,
        MarketplaceDbContext db,
        CancellationToken ct
    )
    {
        await db.Directory.Where(v => v.OrgId == m.OrgId).ExecuteDeleteAsync(ct);
    }
}
