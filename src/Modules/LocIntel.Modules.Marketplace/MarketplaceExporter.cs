using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace;

/// <summary>Marketplace's slice of the offboarding export: the org's side of every request, its vendor profile, its preferred list.</summary>
public sealed class MarketplaceExporter(MarketplaceDbContext db) : IOrgDataExporter
{
    public string Section => "marketplace";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var profile = await db
            .Profiles.IgnoreQueryFilters()
            .Where(p => p.OrgId == org)
            .Select(p => new
            {
                p.Name,
                p.Description,
                p.Categories,
                p.ServiceAreas,
                p.Published,
            })
            .FirstOrDefaultAsync(ct);
        var preferred = await db
            .Preferred.IgnoreQueryFilters()
            .Where(p => p.OrgId == org)
            .Select(p => new
            {
                vendorOrgId = p.VendorOrgId.Value,
                p.Categories,
                p.Blocked,
                p.Notes,
            })
            .ToListAsync(ct);
        var requests = await db
            .Requests.IgnoreQueryFilters()
            .Where(r => r.OrgId == org || r.VendorOrgId == org)
            .Select(r => new
            {
                r.Id,
                role = r.OrgId == org ? "requester" : "vendor",
                category = r.Category.ToString(),
                urgency = r.Urgency.ToString(),
                status = r.Status.ToString(),
                r.SiteName,
                r.Title,
                r.Details,
                r.StartsAt,
                r.EndsAt,
                r.Rrule,
                r.BudgetAmount,
                r.Currency,
                r.CreatedAt,
                r.CompletedAt,
                r.VerifiedAt,
                events = db
                    .Events.IgnoreQueryFilters()
                    .Where(e => e.RequestId == r.Id)
                    .OrderBy(e => e.At)
                    .Select(e => new
                    {
                        kind = e.Kind.ToString(),
                        e.Body,
                        e.At,
                        e.DistanceFromSiteMeters,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new
            {
                profile,
                preferred,
                requests,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
