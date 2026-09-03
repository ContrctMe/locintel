using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Vendors.Messages;
using LocIntel.Platform.Data;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Marketplace.Vendors.Handlers;

/// <summary>Upserts the platform-global directory row (no tenant filter: the table is global by design).</summary>
public static class VendorListingPublishedHandler
{
    [Transactional(typeof(MarketplaceDbContext))]
    public static async Task Handle(
        VendorListingPublished m,
        MarketplaceDbContext db,
        CancellationToken ct
    )
    {
        await db.TakeAsync(m.OrgId.Value, ct);
        var row = await db.Directory.FirstOrDefaultAsync(v => v.OrgId == m.OrgId, ct);
        if (row is not null && row.UpdatedAt > m.UpdatedAt)
            return; // an older projection arriving late never overwrites a newer one
        if (row is null)
        {
            row = new VendorListing { OrgId = m.OrgId, Name = m.Name };
            db.Directory.Add(row);
        }
        row.Name = m.Name;
        row.Description = m.Description;
        row.Categories = m.Categories;
        row.ServiceAreas = m.ServiceAreas;
        row.Latitude = m.Latitude;
        row.Longitude = m.Longitude;
        row.ServiceRadiusKm = m.ServiceRadiusKm;
        row.ContactEmail = m.ContactEmail;
        row.ContactPhone = m.ContactPhone;
        row.CredentialsJson = m.CredentialsJson;
        row.UpdatedAt = m.UpdatedAt;
        await db.SaveChangesAsync(ct);
    }
}
