using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Api;
using LocIntel.Modules.Marketplace.Requests.Messages;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The vendor's side (vendor:fulfill, inside the vendor org), on rows the
/// vendor OWNS (ADR 48): its assignment, its quote, its timeline copies.
/// Every action is a command on the assignment that publishes to the
/// requester, whose answer (RequestStateChanged) is the state that sticks.
/// An assignment exists from submission on; drafts never leave the requester.
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
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.VendorFulfill, ct);
        if (gate.Actor is not { } actor)
            return gate.ToResult();
        // assigned to us, or a broadcast we were invited to and have not bowed out of
        var query = db.Assignments.Where(a =>
            a.Participation != RecipientStatus.Declined
            && a.Participation != RecipientStatus.NotSelected
        );
        if (status is { } s)
            query = query.Where(a => a.Status == s);
        return Results.Ok(
            await RequestViews.VendorListAsync(
                query,
                await OwnName(db, actor.Org, ct),
                limit,
                offset,
                ct
            )
        );
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
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        return Results.Ok(
            await RequestViews.VendorDetailAsync(
                a!,
                await OwnName(db, actor!.Value.Org, ct),
                db,
                actors,
                ct
            )
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
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (a!.Status != RequestStatus.Submitted)
            return Results.Conflict(new { error = "only submitted requests can be accepted" });
        if (a.Mode == RequestMode.Broadcast)
            return Results.Conflict(
                new { error = "quote on a broadcast request; the buyer awards it" }
            );
        // expired credentials block new assignments (blueprint: trust and safety)
        var now = time.GetUtcNow();
        if (await db.Credentials.AnyAsync(c => c.ExpiresAt <= now, ct))
            return Results.Conflict(
                new { error = "renew expired credentials before accepting new work" }
            );
        a.Status = RequestStatus.Accepted;
        a.AcceptedAt = now;
        return await Respond(
            a,
            actor!.Value,
            VendorResponse.Accepted,
            null,
            "Accepted",
            RequestEventKind.StatusChange,
            "marketplace.request_accepted",
            db,
            bus,
            now,
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
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (a!.Status != RequestStatus.Submitted)
            return Results.Conflict(new { error = "only submitted requests can be declined" });
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { error = "declining needs a reason" });
        var reason = request.Reason.Trim();
        var now = time.GetUtcNow();
        if (a.Mode == RequestMode.Broadcast)
        {
            // one recipient bowing out leaves the request open for the others
            a.Participation = RecipientStatus.Declined;
            var own = await db.Quotes.FirstOrDefaultAsync(
                q => q.RequestId == a.RequestId && q.Status == QuoteStatus.Submitted,
                ct
            );
            if (own is not null)
                own.Status = QuoteStatus.Withdrawn;
            return await Respond(
                a,
                actor!.Value,
                VendorResponse.DeclinedToQuote,
                reason,
                $"Declined to quote: {reason}",
                RequestEventKind.Message,
                "marketplace.request_declined",
                db,
                bus,
                now,
                ct
            );
        }
        a.Status = RequestStatus.Declined;
        a.DeclineReason = reason;
        return await Respond(
            a,
            actor!.Value,
            VendorResponse.Declined,
            reason,
            $"Declined: {reason}",
            RequestEventKind.StatusChange,
            "marketplace.request_declined",
            db,
            bus,
            now,
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
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (a!.Status != RequestStatus.Accepted || !a.IsAssigned)
            return Results.Conflict(
                new { error = "only accepted requests assigned to you can be started" }
            );
        var now = time.GetUtcNow();
        a.Status = RequestStatus.InProgress;
        a.StartedAt = now;
        return await Respond(
            a,
            actor!.Value,
            VendorResponse.Started,
            null,
            "Started",
            RequestEventKind.StatusChange,
            "marketplace.request_started",
            db,
            bus,
            now,
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
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (a!.Status != RequestStatus.InProgress || !a.IsAssigned)
            return Results.Conflict(new { error = "only your work in progress can be completed" });
        var now = time.GetUtcNow();
        var summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        a.Status = RequestStatus.Completed;
        a.CompletedAt = now;
        a.CompletionSummary = summary;
        return await Respond(
            a,
            actor!.Value,
            VendorResponse.Completed,
            summary,
            summary is null ? "Completed" : $"Completed: {summary}",
            RequestEventKind.StatusChange,
            "marketplace.request_completed",
            db,
            bus,
            now,
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
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest(new { error = "a message needs a body" });
        return await Append(
            a!,
            actor!.Value,
            RequestEventKind.Message,
            request.Body.Trim(),
            null,
            null,
            null,
            db,
            bus,
            ct
        );
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
        IMessageBus bus,
        CancellationToken ct
    ) => Position(id, request, RequestEventKind.CheckIn, db, accessor, scopes, bus, ct);

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/check-out")]
    [ProducesResponseType(typeof(RequestEventCreated), StatusCodes.Status200OK)]
    public static Task<IResult> CheckOut(
        Guid id,
        CheckInRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    ) => Position(id, request, RequestEventKind.CheckOut, db, accessor, scopes, bus, ct);

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/delivery")]
    [ProducesResponseType(typeof(RequestEventCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Delivery(
        Guid id,
        MessageRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (a!.Status is not (RequestStatus.Accepted or RequestStatus.InProgress))
            return Results.Conflict(
                new { error = "deliveries happen on accepted or in-progress requests" }
            );
        return await Append(
            a,
            actor!.Value,
            RequestEventKind.Delivery,
            request.Body?.Trim(),
            null,
            null,
            null,
            db,
            bus,
            ct
        );
    }

    /// <summary>Quote on a broadcast: the vendor's OWN row, one per request; resubmitting replaces it. The requester projects it.</summary>
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/vendor/requests/{id}/quotes")]
    [ProducesResponseType(typeof(Api.QuoteSubmitted), StatusCodes.Status200OK)]
    public static async Task<IResult> SubmitQuote(
        Guid id,
        SubmitQuoteRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (a!.Mode != RequestMode.Broadcast || a.Status != RequestStatus.Submitted)
            return Results.Conflict(new { error = "quotes are for open broadcast requests" });
        if (request.Amount < 0)
            return Results.BadRequest(new { error = "amount cannot be negative" });
        var now = time.GetUtcNow();
        if (request.ValidUntil is { } until && until <= now)
            return Results.BadRequest(new { error = "validity must be in the future" });
        if (await db.Credentials.AnyAsync(c => c.ExpiresAt <= now, ct))
            return Results.Conflict(new { error = "renew expired credentials before quoting" });
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.RequestId == a.RequestId, ct);
        if (quote is null)
        {
            quote = new Quote
            {
                Id = Guid.CreateVersion7(),
                OrgId = actor!.Value.Org,
                RequesterOrgId = a.RequesterOrgId,
                RequestId = a.RequestId,
                Amount = request.Amount,
                Currency = a.Currency,
                SubmittedBy = actor.Value.Id,
            };
            db.Quotes.Add(quote);
        }
        else if (quote.Status is not (QuoteStatus.Submitted or QuoteStatus.Withdrawn))
            return Results.Conflict(new { error = $"your quote is {quote.Status}" });
        quote.Amount = request.Amount;
        quote.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        quote.ValidUntil = request.ValidUntil;
        quote.Status = QuoteStatus.Submitted;
        quote.UpdatedAt = now;
        a.Participation = RecipientStatus.Quoted;
        a.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(
            a.RequesterOrgId,
            new Messages.QuoteSubmitted(
                a.RequestId,
                actor!.Value.Org,
                await OwnName(db, actor.Value.Org, ct),
                quote.Id,
                quote.Amount,
                quote.Currency,
                quote.Notes,
                quote.ValidUntil,
                now
            )
        );
        await bus.AuditAsync(
            actor.Value.Org,
            actor.Value.Audit,
            "marketplace.quote_submitted",
            new { RequestId = id, quote.Amount }
        );
        return Results.Ok(new Api.QuoteSubmitted(quote.Id, quote.Status));
    }

    private static async Task<IResult> Position(
        Guid id,
        CheckInRequest request,
        RequestEventKind kind,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (a, actor, error) = await Load(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (a!.Status is not (RequestStatus.Accepted or RequestStatus.InProgress))
            return Results.Conflict(
                new { error = "check-ins happen on accepted or in-progress requests" }
            );
        if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
            return Results.BadRequest(new { error = "coordinates out of range" });
        double? distance =
            a.SiteLatitude is { } lat && a.SiteLongitude is { } lng
                ? Geo.DistanceMeters(lat, lng, request.Latitude, request.Longitude)
                : null;
        return await Append(
            a,
            actor!.Value,
            kind,
            request.Note?.Trim(),
            request.Latitude,
            request.Longitude,
            distance,
            db,
            bus,
            ct
        );
    }

    /// <summary>A vendor-side timeline entry: own copy, then the requester's.</summary>
    private static async Task<IResult> Append(
        VendorAssignment a,
        ActorRef actor,
        RequestEventKind kind,
        string? body,
        double? lat,
        double? lng,
        double? distance,
        MarketplaceDbContext db,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var evt = RequestViews.Event(a.OrgId, a.RequestId, actor, kind, body, lat, lng, distance);
        db.Events.Add(evt);
        a.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await RequestFanOut.EventAsync(bus, [a.RequesterOrgId], evt);
        return Results.Ok(
            new RequestEventCreated(
                evt.SourceId,
                distance,
                distance is { } d ? d <= Geo.GeofenceMeters : null
            )
        );
    }

    /// <summary>A vendor-side action: the assignment already carries the optimistic state; record the entry and tell the requester, whose answer is final.</summary>
    private static async Task<IResult> Respond(
        VendorAssignment a,
        ActorRef actor,
        VendorResponse response,
        string? text,
        string eventBody,
        RequestEventKind kind,
        string auditEvent,
        MarketplaceDbContext db,
        IMessageBus bus,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        a.UpdatedAt = now;
        var evt = RequestViews.Event(
            a.OrgId,
            a.RequestId,
            actor,
            kind,
            eventBody,
            null,
            null,
            null
        );
        db.Events.Add(evt);
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(
            a.RequesterOrgId,
            new VendorResponded(
                a.RequestId,
                actor.Org,
                await OwnName(db, actor.Org, ct),
                response,
                text,
                actor.Id,
                evt.SourceId,
                now
            )
        );
        await bus.AuditAsync(
            actor.Org,
            actor.Audit,
            auditEvent,
            new { a.RequestId, Response = response.ToString() }
        );
        return Results.Ok(RequestViews.Mutated(a));
    }

    private static async Task<string> OwnName(
        MarketplaceDbContext db,
        OrgId org,
        CancellationToken ct
    ) =>
        await db.Profiles.Where(p => p.OrgId == org).Select(p => p.Name).FirstOrDefaultAsync(ct)
        ?? "The vendor";

    /// <summary>The vendor's own row for the request, still in play, or a 404.</summary>
    private static async Task<(VendorAssignment?, ActorRef?, IResult?)> Load(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await ActorGate.RequireAsync(accessor, scopes, Capabilities.VendorFulfill, ct);
        if (gate.Actor is not { } actor)
            return (null, null, gate.ToResult());
        var row = await db.Assignments.FirstOrDefaultAsync(
            a =>
                a.RequestId == id
                && a.Participation != RecipientStatus.Declined
                && a.Participation != RecipientStatus.NotSelected,
            ct
        );
        return row is null ? (null, null, Results.NotFound()) : (row, actor, null);
    }
}
