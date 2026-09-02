using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Api;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>Projections shared by both sides; the reader's org decides which actor labels resolve.</summary>
public static class RequestViews
{
    public static async Task<RequestDetail> DetailAsync(
        ServiceRequest request,
        OrgId readerOrg,
        bool canManage,
        MarketplaceDbContext db,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var vendorName = request.VendorOrgId is { } vendorOrg
            ? await db
                .Profiles.Where(p => p.OrgId == vendorOrg)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(ct)
            : null;
        var quotes = await db
            .Quotes.Where(q => q.RequestId == request.Id)
            .OrderBy(q => q.Amount)
            .ToListAsync(ct);
        var recipients = await db
            .Recipients.Where(x => x.RequestId == request.Id)
            .OrderBy(x => x.NotifiedAt)
            .ToListAsync(ct);
        var vendorIds = quotes
            .Select(q => q.VendorOrgId)
            .Concat(recipients.Select(x => x.VendorOrgId))
            .Distinct()
            .ToArray();
        var vendorNames = await db
            .Profiles.Where(p => vendorIds.Contains(p.OrgId))
            .ToDictionaryAsync(p => p.OrgId, p => p.Name, ct);
        // the other side never sees competing quotes: a vendor sees only its own
        var mine = readerOrg == request.OrgId;
        var events = await db
            .Events.Where(e => e.RequestId == request.Id)
            .OrderBy(e => e.At)
            .ToListAsync(ct);
        // labels only for your own org's people: the other party sees a side, not a name
        var labels = await actors.LabelsAsync(
            events.Where(e => e.ActorOrgId == readerOrg).Select(e => e.ActorId).Distinct().ToList(),
            ct
        );
        return new RequestDetail(
            request.Id,
            request.VendorOrgId?.Value,
            vendorName,
            request.Mode,
            request.RequesterName,
            request.Category,
            request.Urgency,
            request.Status,
            request.SiteId,
            request.SiteName,
            request.SiteTimeZone,
            request.SiteLatitude,
            request.SiteLongitude,
            request.Title,
            request.Details,
            JsonSerializer.Deserialize<Dictionary<string, string>>(request.SpecJson) ?? [],
            request.StartsAt,
            request.EndsAt,
            request.Rrule,
            request.BudgetAmount,
            request.Currency,
            request.IncidentId,
            request.CaseId,
            request.CreatedAt,
            request.UpdatedAt,
            request.SubmittedAt,
            request.AcceptedAt,
            request.DeclineReason,
            request.StartedAt,
            request.CompletedAt,
            request.CompletionSummary,
            request.VerifiedAt,
            request.DisputeReason,
            request.CancelledAt,
            request.CancelReason,
            canManage,
            events
                .Select(e => new RequestEventView(
                    e.Id,
                    e.ActorOrgId == request.OrgId ? "requester" : "vendor",
                    e.ActorId,
                    labels.GetValueOrDefault(e.ActorId),
                    e.Kind,
                    e.Body,
                    e.Latitude,
                    e.Longitude,
                    e.DistanceFromSiteMeters,
                    e.DistanceFromSiteMeters is { } d ? d <= Geo.GeofenceMeters : null,
                    e.At
                ))
                .ToList(),
            quotes
                .Where(q => mine || q.VendorOrgId == readerOrg)
                .Select(q => new QuoteView(
                    q.Id,
                    q.VendorOrgId.Value,
                    vendorNames.GetValueOrDefault(q.VendorOrgId),
                    q.Amount,
                    q.Currency,
                    q.Notes,
                    q.ValidUntil,
                    q.Status,
                    q.CreatedAt
                ))
                .ToList(),
            recipients
                .Where(x => mine || x.VendorOrgId == readerOrg)
                .Select(x => new RecipientView(
                    x.VendorOrgId.Value,
                    vendorNames.GetValueOrDefault(x.VendorOrgId),
                    x.Status,
                    x.NotifiedAt
                ))
                .ToList()
        );
    }

    public static async Task<RequestListResponse> ListAsync(
        IQueryable<ServiceRequest> query,
        MarketplaceDbContext db,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var items = await query
            .OrderByDescending(r => r.UpdatedAt)
            .ThenByDescending(r => r.Id)
            .Skip(skip)
            .Take(take)
            .Select(r => new RequestSummary(
                r.Id,
                r.VendorOrgId == null ? null : r.VendorOrgId.Value.Value,
                r.VendorOrgId == null
                    ? null
                    : db
                        .Profiles.Where(p => p.OrgId == r.VendorOrgId)
                        .Select(p => p.Name)
                        .FirstOrDefault(),
                r.Mode,
                r.RequesterName,
                r.Category,
                r.Urgency,
                r.Status,
                r.SiteId,
                r.SiteName,
                r.Title,
                r.StartsAt,
                r.EndsAt,
                r.BudgetAmount,
                r.Currency,
                r.UpdatedAt
            ))
            .ToListAsync(ct);
        return new RequestListResponse(
            items,
            total,
            skip + items.Count < total ? skip + items.Count : null
        );
    }

    public static RequestEvent StatusEvent(ServiceRequest request, ActorRef actor, string body) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = request.OrgId,
            VendorOrgId = request.VendorOrgId,
            RequestId = request.Id,
            ActorOrgId = actor.Org,
            ActorId = actor.Id,
            Kind = RequestEventKind.StatusChange,
            Body = body,
        };

    public static RequestMutated Mutated(ServiceRequest request) =>
        new(request.Id, request.Status, request.UpdatedAt);

    public static IQueryable<ServiceRequest> InScope(
        IQueryable<ServiceRequest> query,
        NodeScope scope
    )
    {
        switch (scope)
        {
            case NodeScope.EntireOrg org:
                return query.Where(r => r.OrgId == org.Org);
            case NodeScope.Subtrees s:
                var paths = s.Paths.Select(p => new LTree(p)).ToArray();
                return query.Where(r =>
                    r.OrgId == s.Org && paths.Any(p => r.Path.IsDescendantOf(p))
                );
            default:
                return query.Where(r => false);
        }
    }
}
