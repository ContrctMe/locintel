using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace;

/// <summary>Marketplace's slice of the offboarding export: what the org OWNS - its requests, its assignments as a vendor, its quotes, its profile, its preferred list (ADR 48: nothing here belongs to another org).</summary>
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
            .Where(r => r.OrgId == org)
            .Select(r => new
            {
                r.Id,
                vendorOrgId = r.VendorOrgId == null ? (Guid?)null : r.VendorOrgId.Value.Value,
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
                    .Where(e => e.OrgId == org && e.RequestId == r.Id)
                    .OrderBy(e => e.At)
                    .Select(e => new
                    {
                        kind = e.Kind.ToString(),
                        e.Body,
                        e.At,
                        e.DistanceFromSiteMeters,
                    })
                    .ToList(),
                quotes = db
                    .ReceivedQuotes.IgnoreQueryFilters()
                    .Where(q => q.OrgId == org && q.RequestId == r.Id)
                    .Select(q => new
                    {
                        vendorOrgId = q.VendorOrgId.Value,
                        q.VendorName,
                        q.Amount,
                        q.Currency,
                        status = q.Status.ToString(),
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        var assignments = await db
            .Assignments.IgnoreQueryFilters()
            .Where(a => a.OrgId == org)
            .Select(a => new
            {
                a.RequestId,
                requesterOrgId = a.RequesterOrgId.Value,
                a.RequesterName,
                category = a.Category.ToString(),
                status = a.Status.ToString(),
                participation = a.Participation.ToString(),
                a.SiteName,
                a.Title,
                a.StartsAt,
                a.CompletedAt,
                events = db
                    .Events.IgnoreQueryFilters()
                    .Where(e => e.OrgId == org && e.RequestId == a.RequestId)
                    .OrderBy(e => e.At)
                    .Select(e => new
                    {
                        kind = e.Kind.ToString(),
                        e.Body,
                        e.At,
                        e.DistanceFromSiteMeters,
                    })
                    .ToList(),
                quote = db
                    .Quotes.IgnoreQueryFilters()
                    .Where(q => q.OrgId == org && q.RequestId == a.RequestId)
                    .Select(q => new
                    {
                        q.Amount,
                        q.Currency,
                        status = q.Status.ToString(),
                    })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new
            {
                profile,
                preferred,
                requests,
                assignments,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}
