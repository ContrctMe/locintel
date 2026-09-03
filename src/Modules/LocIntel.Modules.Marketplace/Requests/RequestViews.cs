using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Api;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// Each side reads the API shape from the rows IT owns (ADR 48): the
/// requester from its request, recipients, received quotes and timeline
/// copies; the vendor from its assignment, its own quote and its copies.
/// The reader's org decides which actor labels resolve.
/// </summary>
public static class RequestViews
{
    public static async Task<RequestDetail> RequesterDetailAsync(
        ServiceRequest request,
        bool canManage,
        MarketplaceDbContext db,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var quotes = await db
            .ReceivedQuotes.Where(q => q.RequestId == request.Id)
            .OrderBy(q => q.Amount)
            .ToListAsync(ct);
        var recipients = await db
            .Recipients.Where(x => x.RequestId == request.Id)
            .OrderBy(x => x.NotifiedAt)
            .ToListAsync(ct);
        var vendorIds = recipients
            .Select(x => x.VendorOrgId)
            .Concat(request.VendorOrgId is { } v ? [v] : [])
            .Distinct()
            .ToArray();
        var names = await db
            .Directory.Where(p => vendorIds.Contains(p.OrgId))
            .ToDictionaryAsync(p => p.OrgId, p => p.Name, ct);
        foreach (var q in quotes)
            names.TryAdd(q.VendorOrgId, q.VendorName);
        var vendorName = request.VendorOrgId is { } vendor ? names.GetValueOrDefault(vendor) : null;
        var events = await db
            .Events.Where(e => e.RequestId == request.Id)
            .OrderBy(e => e.At)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            events
                .Where(e => e.ActorOrgId == request.OrgId)
                .Select(e => e.ActorId)
                .Distinct()
                .ToList(),
            ct
        );
        return Detail(
            request.Snapshot(),
            request.OrgId,
            request.VendorOrgId?.Value,
            vendorName,
            request.IncidentId,
            request.CaseId,
            request.CreatedAt,
            canManage,
            events,
            labels,
            quotes
                .Select(q => new QuoteView(
                    q.QuoteId,
                    q.VendorOrgId.Value,
                    q.VendorName,
                    q.Amount,
                    q.Currency,
                    q.Notes,
                    q.ValidUntil,
                    q.Status,
                    q.SubmittedAt
                ))
                .ToList(),
            recipients
                .Select(x => new RecipientView(
                    x.VendorOrgId.Value,
                    names.GetValueOrDefault(x.VendorOrgId),
                    x.Status,
                    x.NotifiedAt
                ))
                .ToList()
        );
    }

    public static async Task<RequestDetail> VendorDetailAsync(
        VendorAssignment a,
        string ownName,
        MarketplaceDbContext db,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.RequestId == a.RequestId, ct);
        var events = await db
            .Events.Where(e => e.RequestId == a.RequestId)
            .OrderBy(e => e.At)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            events.Where(e => e.ActorOrgId == a.OrgId).Select(e => e.ActorId).Distinct().ToList(),
            ct
        );
        return Detail(
            Snapshot(a),
            a.RequesterOrgId,
            a.IsAssigned ? a.OrgId.Value : null,
            a.IsAssigned ? ownName : null,
            null,
            null,
            a.CreatedAt,
            true,
            events,
            labels,
            quote is null
                ? [] // the other side's quotes are theirs: a vendor sees only its own
                :
                [
                    new QuoteView(
                        quote.Id,
                        a.OrgId.Value,
                        ownName,
                        quote.Amount,
                        quote.Currency,
                        quote.Notes,
                        quote.ValidUntil,
                        quote.Status,
                        quote.CreatedAt
                    ),
                ],
            a.Mode == RequestMode.Broadcast
                ? [new RecipientView(a.OrgId.Value, ownName, a.Participation, a.CreatedAt)]
                : []
        );
    }

    private static RequestDetail Detail(
        RequestSnapshot s,
        OrgId requester,
        Guid? vendorOrgId,
        string? vendorName,
        Guid? incidentId,
        Guid? caseId,
        DateTimeOffset createdAt,
        bool canManage,
        List<RequestEvent> events,
        IReadOnlyDictionary<Guid, string> labels,
        List<QuoteView> quotes,
        List<RecipientView> recipients
    ) =>
        new(
            s.RequestId,
            vendorOrgId,
            vendorName,
            s.Mode,
            s.RequesterName,
            s.Category,
            s.Urgency,
            s.Status,
            s.SiteId,
            s.SiteName,
            s.SiteTimeZone,
            s.SiteLatitude,
            s.SiteLongitude,
            s.Title,
            s.Details,
            JsonSerializer.Deserialize<Dictionary<string, string>>(s.SpecJson) ?? [],
            s.StartsAt,
            s.EndsAt,
            s.Rrule,
            s.BudgetAmount,
            s.Currency,
            incidentId,
            caseId,
            createdAt,
            s.UpdatedAt,
            s.SubmittedAt,
            s.ResponseDueAt,
            s.EscalatedAt,
            s.EscalationCount,
            s.AcceptedAt,
            s.DeclineReason,
            s.StartedAt,
            s.CompletedAt,
            s.CompletionSummary,
            s.VerifiedAt,
            s.DisputeReason,
            s.CancelledAt,
            s.CancelReason,
            canManage,
            events
                .Select(e => new RequestEventView(
                    e.SourceId,
                    e.ActorOrgId == requester ? "requester" : "vendor",
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
            quotes,
            recipients
        );

    private static RequestSnapshot Snapshot(VendorAssignment a) =>
        new(
            a.RequestId,
            a.RequesterOrgId,
            a.RequesterName,
            a.IsAssigned ? a.OrgId : null,
            a.Mode,
            a.Category,
            a.Urgency,
            a.Status,
            a.SiteId,
            a.SiteName,
            a.SiteTimeZone,
            a.SiteLatitude,
            a.SiteLongitude,
            a.SiteCountryCode,
            a.Title,
            a.Details,
            a.SpecJson,
            a.StartsAt,
            a.EndsAt,
            a.Rrule,
            a.BudgetAmount,
            a.Currency,
            a.SubmittedAt,
            a.ResponseDueAt,
            a.EscalatedAt,
            a.EscalationCount,
            a.AcceptedAt,
            a.DeclineReason,
            a.StartedAt,
            a.CompletedAt,
            a.CompletionSummary,
            a.VerifiedAt,
            a.DisputeReason,
            a.CancelledAt,
            a.CancelReason,
            a.UpdatedAt
        );

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
                        .Directory.Where(p => p.OrgId == r.VendorOrgId)
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
                r.UpdatedAt,
                r.ResponseDueAt
            ))
            .ToListAsync(ct);
        return new RequestListResponse(
            items,
            total,
            skip + items.Count < total ? skip + items.Count : null
        );
    }

    public static async Task<RequestListResponse> VendorListAsync(
        IQueryable<VendorAssignment> query,
        string ownName,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var rows = await query
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
        var items = rows.Select(a => new RequestSummary(
                a.RequestId,
                a.IsAssigned ? a.OrgId.Value : null,
                a.IsAssigned ? ownName : null,
                a.Mode,
                a.RequesterName,
                a.Category,
                a.Urgency,
                a.Status,
                a.SiteId,
                a.SiteName,
                a.Title,
                a.StartsAt,
                a.EndsAt,
                a.BudgetAmount,
                a.Currency,
                a.UpdatedAt,
                a.ResponseDueAt
            ))
            .ToList();
        return new RequestListResponse(
            items,
            total,
            skip + items.Count < total ? skip + items.Count : null
        );
    }

    /// <summary>The requester's own timeline entry; fan it out with RequestFanOut.EventAsync.</summary>
    public static RequestEvent StatusEvent(ServiceRequest request, ActorRef actor, string body) =>
        Event(
            request.OrgId,
            request.Id,
            actor,
            RequestEventKind.StatusChange,
            body,
            null,
            null,
            null
        );

    public static RequestEvent Event(
        OrgId holder,
        Guid requestId,
        ActorRef actor,
        RequestEventKind kind,
        string? body,
        double? lat,
        double? lng,
        double? distance
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = holder,
            SourceId = Guid.CreateVersion7(),
            RequestId = requestId,
            ActorOrgId = actor.Org,
            ActorId = actor.Id,
            Kind = kind,
            Body = body,
            Latitude = lat,
            Longitude = lng,
            DistanceFromSiteMeters = distance,
        };

    public static RequestMutated Mutated(ServiceRequest request) =>
        new(request.Id, request.Status, request.UpdatedAt);

    public static RequestMutated Mutated(VendorAssignment a) =>
        new(a.RequestId, a.Status, a.UpdatedAt);

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
