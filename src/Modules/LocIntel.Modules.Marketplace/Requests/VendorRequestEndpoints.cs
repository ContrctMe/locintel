using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Api;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The vendor's side (vendor:fulfill, inside the vendor org): the queue of
/// requests addressed to them, accept/decline, start, check-in/out with
/// geofence distance, complete. A request is visible from submission on;
/// drafts never leave the requester.
/// </summary>
public static class VendorRequestEndpoints
{
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/vendor/requests")]
    [ProducesResponseType(typeof(RequestListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        RequestStatus? status,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.VendorFulfill, ct))
            return Results.Unauthorized();
        var query = db.Requests.Where(r =>
            r.VendorOrgId == actor.Org && r.Status != RequestStatus.Draft
        );
        if (status is { } s)
            query = query.Where(r => r.Status == s);
        return Results.Ok(await RequestViews.ListAsync(query, db, limit, offset, ct));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/vendor/requests/{id}")]
    [ProducesResponseType(typeof(RequestDetail), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        return Results.Ok(
            await RequestViews.DetailAsync(row!, actor!.Value.Org, true, db, actors, ct)
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/accept")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Accept(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (row!.Status != RequestStatus.Submitted)
            return Results.Conflict(new { error = "only submitted requests can be accepted" });
        // expired credentials block new assignments (blueprint: trust and safety)
        var now = time.GetUtcNow();
        if (
            await db.Credentials.AnyAsync(
                c => c.OrgId == actor!.Value.Org && c.ExpiresAt <= now,
                ct
            )
        )
            return Results.Conflict(
                new { error = "renew expired credentials before accepting new work" }
            );
        return await Transition(
            row,
            actor!.Value,
            RequestStatus.Accepted,
            "Accepted",
            r => r.AcceptedAt = now,
            db,
            bus,
            $"Request accepted: {row.Title}",
            [$"{VendorName(db, row)} accepted the request for {row.SiteName}."],
            "marketplace.request_accepted",
            ct
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/decline")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Decline(
        Guid id,
        ReasonRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (row!.Status != RequestStatus.Submitted)
            return Results.Conflict(new { error = "only submitted requests can be declined" });
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { error = "declining needs a reason" });
        var reason = request.Reason.Trim();
        return await Transition(
            row,
            actor!.Value,
            RequestStatus.Declined,
            $"Declined: {reason}",
            r => r.DeclineReason = reason,
            db,
            bus,
            $"Request declined: {row.Title}",
            [$"{VendorName(db, row)} declined: {reason}"],
            "marketplace.request_declined",
            ct
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/start")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Start(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (row!.Status != RequestStatus.Accepted)
            return Results.Conflict(new { error = "only accepted requests can be started" });
        var now = time.GetUtcNow();
        return await Transition(
            row,
            actor!.Value,
            RequestStatus.InProgress,
            "Started",
            r => r.StartedAt = now,
            db,
            bus,
            $"Work started: {row.Title}",
            [$"{VendorName(db, row)} started work at {row.SiteName}."],
            "marketplace.request_started",
            ct
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/complete")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Complete(
        Guid id,
        CompleteRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (row!.Status != RequestStatus.InProgress)
            return Results.Conflict(new { error = "only work in progress can be completed" });
        var now = time.GetUtcNow();
        var summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        return await Transition(
            row,
            actor!.Value,
            RequestStatus.Completed,
            summary is null ? "Completed" : $"Completed: {summary}",
            r =>
            {
                r.CompletedAt = now;
                r.CompletionSummary = summary;
            },
            db,
            bus,
            $"Work completed: {row.Title}",
            [
                $"{VendorName(db, row)} reports the work at {row.SiteName} complete.",
                "Verify or dispute it in the console.",
            ],
            "marketplace.request_completed",
            ct
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/messages")]
    [ProducesResponseType(typeof(RequestEventCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Message(
        Guid id,
        MessageRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest(new { error = "a message needs a body" });
        var evt = NewEvent(
            row!,
            actor!.Value,
            RequestEventKind.Message,
            request.Body.Trim(),
            null,
            null,
            null
        );
        db.Events.Add(evt);
        row!.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new RequestEventCreated(evt.Id, null, null));
    }

    /// <summary>Guard check-in: position kept, distance to the site computed when both sides have coordinates.</summary>
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/check-in")]
    [ProducesResponseType(typeof(RequestEventCreated), StatusCodes.Status200OK)]
    public static Task<IResult> CheckIn(
        Guid id,
        CheckInRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    ) => Position(id, request, RequestEventKind.CheckIn, db, accessor, scopes, ct);

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/check-out")]
    [ProducesResponseType(typeof(RequestEventCreated), StatusCodes.Status200OK)]
    public static Task<IResult> CheckOut(
        Guid id,
        CheckInRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    ) => Position(id, request, RequestEventKind.CheckOut, db, accessor, scopes, ct);

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/delivery")]
    [ProducesResponseType(typeof(RequestEventCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Delivery(
        Guid id,
        MessageRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (row!.Status is not (RequestStatus.Accepted or RequestStatus.InProgress))
            return Results.Conflict(
                new { error = "deliveries happen on accepted or in-progress requests" }
            );
        var evt = NewEvent(
            row,
            actor!.Value,
            RequestEventKind.Delivery,
            request.Body?.Trim(),
            null,
            null,
            null
        );
        db.Events.Add(evt);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new RequestEventCreated(evt.Id, null, null));
    }

    private static async Task<IResult> Position(
        Guid id,
        CheckInRequest request,
        RequestEventKind kind,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (row, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (row!.Status is not (RequestStatus.Accepted or RequestStatus.InProgress))
            return Results.Conflict(
                new { error = "check-ins happen on accepted or in-progress requests" }
            );
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return Results.BadRequest(new { error = "coordinates out of range" });
        double? distance =
            row.SiteLatitude is { } lat && row.SiteLongitude is { } lng
                ? Geo.DistanceMeters(lat, lng, request.Latitude, request.Longitude)
                : null;
        var evt = NewEvent(
            row,
            actor!.Value,
            kind,
            request.Note?.Trim(),
            request.Latitude,
            request.Longitude,
            distance
        );
        db.Events.Add(evt);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(
            new RequestEventCreated(
                evt.Id,
                distance,
                distance is { } d ? d <= Geo.GeofenceMeters : null
            )
        );
    }

    private static async Task<IResult> Transition(
        ServiceRequest row,
        ActorRef actor,
        RequestStatus status,
        string eventBody,
        Action<ServiceRequest> apply,
        MarketplaceDbContext db,
        IMessageBus bus,
        string noticeSubject,
        string[] noticeLines,
        string auditEvent,
        CancellationToken ct
    )
    {
        row.Status = status;
        apply(row);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        db.Events.Add(RequestViews.StatusEvent(row, actor, eventBody));
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(noticeSubject, noticeLines),
            new DeliveryOptions { TenantId = row.OrgId.Value.ToString() }
        );
        await MarketplaceAudit.PublishAsync(
            bus,
            actor,
            auditEvent,
            new { row.Id, Status = status.ToString() }
        );
        return Results.Ok(RequestViews.Mutated(row));
    }

    private static RequestEvent NewEvent(
        ServiceRequest row,
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
            OrgId = row.OrgId,
            VendorOrgId = row.VendorOrgId,
            RequestId = row.Id,
            ActorOrgId = actor.Org,
            ActorId = actor.Id,
            Kind = kind,
            Body = body,
            Latitude = lat,
            Longitude = lng,
            DistanceFromSiteMeters = distance,
        };

    private static string VendorName(MarketplaceDbContext db, ServiceRequest row) =>
        db.Profiles.Where(p => p.OrgId == row.VendorOrgId).Select(p => p.Name).FirstOrDefault()
        ?? "The vendor";

    /// <summary>The vendor side of a row: addressed to the caller's org and past Draft, or a 404.</summary>
    private static async Task<(ServiceRequest?, ActorRef?, IResult?)> Load(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return (null, null, Results.Unauthorized());
        if (!await scopes.CanAsync(accessor.Current, Capabilities.VendorFulfill, ct))
            return (null, null, Results.Unauthorized());
        var row = await db.Requests.FirstOrDefaultAsync(
            r => r.Id == id && r.VendorOrgId == actor.Org && r.Status != RequestStatus.Draft,
            ct
        );
        return row is null ? (null, null, Results.NotFound()) : (row, actor, null);
    }
}
