using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Marketplace.Data;
using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Modules.Marketplace.Requests.Api;
using LocIntel.Modules.Marketplace.Vendors;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The buyer's side. Gate 1 is real here: marketplace.enabled (402 with an
/// upsell code) guards raising and submitting. marketplace:read lists what
/// is in the reader's site scope; marketplace:manage raises, submits,
/// cancels, verifies, disputes.
/// </summary>
public static class RequesterEndpoints
{
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/marketplace/requests")]
    [ProducesResponseType(typeof(RequestListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        RequestStatus? status,
        ServiceCategory? category,
        Guid? siteId,
        Guid? incidentId,
        Guid? caseId,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.MarketplaceRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var query = RequestViews.InScope(db.Requests, scope);
        if (status is { } s)
            query = query.Where(r => r.Status == s);
        if (category is { } c)
            query = query.Where(r => r.Category == c);
        if (siteId is { } site)
            query = query.Where(r => r.SiteId == site);
        if (incidentId is { } incident)
            query = query.Where(r => r.IncidentId == incident);
        if (caseId is { } @case)
            query = query.Where(r => r.CaseId == @case);
        return Results.Ok(await RequestViews.ListAsync(query, db, limit, offset, ct));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverineGet("/api/marketplace/requests/{id}")]
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
        var (request, actor, manage, error) = await Load(id, db, accessor, scopes, false, ct);
        if (error is not null)
            return error;
        return Results.Ok(
            await RequestViews.DetailAsync(request!, actor!.Value.Org, manage, db, actors, ct)
        );
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests")]
    [ProducesResponseType(typeof(RequestCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        ISiteDirectory sites,
        IOrganizationLookup orgs,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var scope = await scopes.ScopeForAsync(
            accessor.Current,
            Capabilities.MarketplaceManage,
            ct
        );
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        if (await Upsell(entitlements, actor.Org, ct) is { } upsell)
            return upsell;
        var site = await sites.FindAsync(request.SiteId, ct);
        if (site is null || !scope.Covers(site.Path))
            return Results.NotFound();
        OrgId? vendorOrg = request.VendorOrgId is { } chosen ? new OrgId(chosen) : null;
        if (vendorOrg == actor.Org)
            return Results.BadRequest(new { error = "an org cannot hire itself" });
        if (vendorOrg is { } direct)
        {
            var vendor = await db.Profiles.FirstOrDefaultAsync(
                p => p.OrgId == direct && p.Published,
                ct
            );
            if (vendor is null)
                return Results.NotFound();
            if (!vendor.Offers(request.Category))
                return Results.BadRequest(
                    new { error = "that vendor does not offer this category" }
                );
            if (await db.Preferred.AnyAsync(p => p.VendorOrgId == direct && p.Blocked, ct))
                return Results.BadRequest(new { error = "that vendor is blocked by your org" });
        }
        else if (
            (
                await VendorMatcher.MatchAsync(
                    db,
                    actor.Org,
                    request.Category,
                    site.Latitude,
                    site.Longitude,
                    site.CountryCode,
                    ct
                )
            ).Count == 0
        )
            return Results.Conflict(
                new { error = "no published vendor serves this category at that site" }
            );
        if (
            Validate(
                request.Title,
                request.StartsAt,
                request.EndsAt,
                request.Urgency,
                request.Rrule,
                request.Currency,
                request.BudgetAmount
            ) is
            { } invalid
        )
            return Results.BadRequest(new { error = invalid });
        var requester = await orgs.GetAsync(actor.Org, ct);

        var row = new ServiceRequest
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            VendorOrgId = vendorOrg,
            Mode = vendorOrg is null ? RequestMode.Broadcast : RequestMode.Direct,
            Category = request.Category,
            Urgency = request.Urgency,
            SiteId = site.Id,
            SiteName = site.Name,
            SiteTimeZone = site.TimeZone,
            SiteLatitude = site.Latitude,
            SiteLongitude = site.Longitude,
            SiteCountryCode = site.CountryCode,
            Path = new LTree(site.Path),
            RequesterName = requester?.Name ?? "Requester",
            Title = request.Title.Trim(),
            Details = request.Details?.Trim() ?? "",
            SpecJson = JsonSerializer.Serialize(CleanSpec(request.Spec)),
            StartsAt = request.StartsAt.ToUniversalTime(),
            EndsAt = request.EndsAt?.ToUniversalTime(),
            Rrule = string.IsNullOrWhiteSpace(request.Rrule) ? null : request.Rrule.Trim(),
            BudgetAmount = request.BudgetAmount,
            Currency = (request.Currency ?? "USD").ToUpperInvariant(),
            IncidentId = request.IncidentId,
            CaseId = request.CaseId,
            CreatedBy = actor.Id,
        };
        db.Requests.Add(row);
        await db.SaveChangesAsync(ct);
        await MarketplaceAudit.PublishAsync(
            bus,
            actor,
            "marketplace.request_created",
            new
            {
                row.Id,
                Category = row.Category.ToString(),
                VendorOrgId = vendorOrg?.Value,
                Mode = row.Mode.ToString(),
                row.SiteId,
            }
        );
        return Results.Ok(new RequestCreated(row.Id, row.Status));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePut("/api/marketplace/requests/{id}")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (row, _, _, error) = await Load(id, db, accessor, scopes, true, ct);
        if (error is not null)
            return error;
        if (row!.Status != RequestStatus.Draft)
            return Results.Conflict(new { error = "only drafts can be edited" });
        if (
            Validate(
                request.Title,
                request.StartsAt,
                request.EndsAt,
                request.Urgency,
                request.Rrule,
                request.Currency,
                request.BudgetAmount
            ) is
            { } invalid
        )
            return Results.BadRequest(new { error = invalid });
        row.Urgency = request.Urgency;
        row.Title = request.Title.Trim();
        row.Details = request.Details?.Trim() ?? "";
        row.SpecJson = JsonSerializer.Serialize(CleanSpec(request.Spec));
        row.StartsAt = request.StartsAt.ToUniversalTime();
        row.EndsAt = request.EndsAt?.ToUniversalTime();
        row.Rrule = string.IsNullOrWhiteSpace(request.Rrule) ? null : request.Rrule.Trim();
        row.BudgetAmount = request.BudgetAmount;
        row.Currency = (request.Currency ?? row.Currency).ToUpperInvariant();
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(RequestViews.Mutated(row));
    }

    /// <summary>Submit: the vendor is notified; the request leaves the requester's hands.</summary>
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests/{id}/submit")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Submit(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        IOptions<MarketplaceOptions> options,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (row, actor, _, error) = await Load(id, db, accessor, scopes, true, ct);
        if (error is not null)
            return error;
        if (await Upsell(entitlements, actor!.Value.Org, ct) is { } upsell)
            return upsell;
        if (row!.Status != RequestStatus.Draft)
            return Results.Conflict(new { error = "only drafts can be submitted" });
        var now = DateTimeOffset.UtcNow;
        List<OrgId> recipients;
        if (row.Mode == RequestMode.Direct)
        {
            var vendor = await db.Profiles.FirstOrDefaultAsync(
                p => p.OrgId == row.VendorOrgId && p.Published,
                ct
            );
            if (vendor is null)
                return Results.Conflict(new { error = "the vendor is no longer published" });
            recipients = [row.VendorOrgId!.Value];
        }
        else
        {
            // broadcast: every matching vendor gets a recipient row (which is
            // what lets them see the request) - preferred vendors first, ten at most
            recipients = (
                await VendorMatcher.MatchAsync(
                    db,
                    actor.Value.Org,
                    row.Category,
                    row.SiteLatitude,
                    row.SiteLongitude,
                    row.SiteCountryCode,
                    ct
                )
            )
                .Take(options.Value.InitialRecipients)
                .ToList();
            if (recipients.Count == 0)
                return Results.Conflict(
                    new { error = "no published vendor serves this category at that site" }
                );
            foreach (var vendorOrg in recipients)
                db.Recipients.Add(
                    new RequestRecipient
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = row.OrgId,
                        VendorOrgId = vendorOrg,
                        RequestId = row.Id,
                    }
                );
        }
        row.Status = RequestStatus.Submitted;
        row.SubmittedAt = now;
        row.ResponseDueAt = now + options.Value.Window(row.Urgency);
        row.UpdatedAt = now;
        db.Events.Add(
            RequestViews.StatusEvent(
                row,
                actor.Value,
                row.Mode == RequestMode.Broadcast
                    ? $"Sent to {recipients.Count} vendor(s) for quotes"
                    : "Submitted"
            )
        );
        await db.SaveChangesAsync(ct);
        foreach (var vendorOrg in recipients)
            await bus.PublishAsync(
                new SendOrgNotice(
                    row.Mode == RequestMode.Broadcast
                        ? $"Request for quotes: {Label(row.Category)} - {row.Title}"
                        : $"New {Label(row.Category)} request: {row.Title}",
                    [
                        $"{row.RequesterName} sent a {row.Urgency.ToString().ToLowerInvariant()} request for {row.SiteName}.",
                        $"Starts {row.StartsAt:u}.",
                        row.Mode == RequestMode.Broadcast
                            ? "Open the vendor console to quote or decline."
                            : "Open the vendor console to accept or decline.",
                    ],
                    "marketplace",
                    row.Mode == RequestMode.Broadcast
                        ? $"Request for quotes: {Label(row.Category)} - {row.Title}"
                        : $"New {Label(row.Category)} request: {row.Title}"
                ),
                new DeliveryOptions { TenantId = vendorOrg.Value.ToString() }
            );
        await MarketplaceAudit.PublishAsync(
            bus,
            actor.Value,
            "marketplace.request_submitted",
            new { row.Id }
        );
        return Results.Ok(RequestViews.Mutated(row));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests/{id}/cancel")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Cancel(
        Guid id,
        ReasonRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (row, actor, _, error) = await Load(id, db, accessor, scopes, true, ct);
        if (error is not null)
            return error;
        if (
            row!.Status
            is not (RequestStatus.Draft or RequestStatus.Submitted or RequestStatus.Accepted)
        )
            return Results.Conflict(
                new { error = "only draft, submitted, or accepted requests can be cancelled" }
            );
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { error = "cancelling needs a reason" });
        var wasVisibleToVendor = row.Status != RequestStatus.Draft;
        var now = DateTimeOffset.UtcNow;
        row.Status = RequestStatus.Cancelled;
        row.CancelledAt = now;
        row.CancelReason = request.Reason.Trim();
        row.UpdatedAt = now;
        db.Events.Add(
            RequestViews.StatusEvent(row, actor!.Value, $"Cancelled: {row.CancelReason}")
        );
        await db.SaveChangesAsync(ct);
        if (wasVisibleToVendor)
            await bus.PublishAsync(
                new SendOrgNotice(
                    $"Request cancelled: {row.Title}",
                    [$"{row.RequesterName} cancelled: {row.CancelReason}"],
                    "marketplace",
                    $"Request cancelled: {row.Title}"
                ),
                new DeliveryOptions { TenantId = row.VendorOrgId!.Value.Value.ToString() }
            );
        await MarketplaceAudit.PublishAsync(
            bus,
            actor.Value,
            "marketplace.request_cancelled",
            new { row.Id, row.CancelReason }
        );
        return Results.Ok(RequestViews.Mutated(row));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests/{id}/verify")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Verify(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (row, actor, _, error) = await Load(id, db, accessor, scopes, true, ct);
        if (error is not null)
            return error;
        if (row!.Status is not (RequestStatus.Completed or RequestStatus.Disputed))
            return Results.Conflict(
                new { error = "only completed or disputed work can be verified" }
            );
        var now = DateTimeOffset.UtcNow;
        row.Status = RequestStatus.Verified;
        row.VerifiedAt = now;
        row.UpdatedAt = now;
        db.Events.Add(RequestViews.StatusEvent(row, actor!.Value, "Verified"));
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(
                $"Work verified: {row.Title}",
                [$"{row.RequesterName} verified the work at {row.SiteName}."],
                "marketplace",
                $"Work verified: {row.Title}"
            ),
            new DeliveryOptions { TenantId = row.VendorOrgId!.Value.Value.ToString() }
        );
        await MarketplaceAudit.PublishAsync(
            bus,
            actor.Value,
            "marketplace.request_verified",
            new { row.Id }
        );
        return Results.Ok(RequestViews.Mutated(row));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests/{id}/dispute")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Dispute(
        Guid id,
        ReasonRequest request,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (row, actor, _, error) = await Load(id, db, accessor, scopes, true, ct);
        if (error is not null)
            return error;
        if (row!.Status != RequestStatus.Completed)
            return Results.Conflict(new { error = "only completed work can be disputed" });
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { error = "a dispute needs a reason" });
        var now = DateTimeOffset.UtcNow;
        row.Status = RequestStatus.Disputed;
        row.DisputeReason = request.Reason.Trim();
        row.UpdatedAt = now;
        db.Events.Add(
            RequestViews.StatusEvent(row, actor!.Value, $"Disputed: {row.DisputeReason}")
        );
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(
                $"Work disputed: {row.Title}",
                [$"{row.RequesterName} disputed: {row.DisputeReason}"],
                "marketplace",
                $"Work disputed: {row.Title}"
            ),
            new DeliveryOptions { TenantId = row.VendorOrgId!.Value.Value.ToString() }
        );
        await MarketplaceAudit.PublishAsync(
            bus,
            actor.Value,
            "marketplace.request_disputed",
            new { row.Id, row.DisputeReason }
        );
        return Results.Ok(RequestViews.Mutated(row));
    }

    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests/{id}/messages")]
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
        var (row, actor, _, error) = await Load(id, db, accessor, scopes, true, ct);
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest(new { error = "a message needs a body" });
        var evt = new RequestEvent
        {
            Id = Guid.CreateVersion7(),
            OrgId = row!.OrgId,
            VendorOrgId = row.VendorOrgId,
            RequestId = row.Id,
            ActorOrgId = actor!.Value.Org,
            ActorId = actor.Value.Id,
            Kind = RequestEventKind.Message,
            Body = request.Body.Trim(),
        };
        db.Events.Add(evt);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new RequestEventCreated(evt.Id, null, null));
    }

    /// <summary>Award a broadcast: the quote's vendor becomes THE vendor, the request is Accepted, other quotes are rejected.</summary>
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests/{id}/quotes/{quoteId}/accept")]
    [ProducesResponseType(typeof(RequestMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> AcceptQuote(
        Guid id,
        Guid quoteId,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (row, actor, _, error) = await Load(id, db, accessor, scopes, true, ct);
        if (error is not null)
            return error;
        if (row!.Mode != RequestMode.Broadcast || row.Status != RequestStatus.Submitted)
            return Results.Conflict(
                new { error = "only an open broadcast request can be awarded" }
            );
        var quotes = await db.Quotes.Where(q => q.RequestId == id).ToListAsync(ct);
        var winner = quotes.FirstOrDefault(q =>
            q.Id == quoteId && q.Status == QuoteStatus.Submitted
        );
        if (winner is null)
            return Results.NotFound();
        if (winner.ValidUntil is { } until && until < DateTimeOffset.UtcNow)
            return Results.Conflict(new { error = "that quote has expired" });
        var now = DateTimeOffset.UtcNow;
        winner.Status = QuoteStatus.Accepted;
        winner.UpdatedAt = now;
        var losers = quotes
            .Where(q => q.Id != quoteId && q.Status == QuoteStatus.Submitted)
            .ToList();
        foreach (var other in losers)
        {
            other.Status = QuoteStatus.Rejected;
            other.UpdatedAt = now;
        }
        row.VendorOrgId = winner.VendorOrgId;
        row.BudgetAmount = winner.Amount;
        row.Currency = winner.Currency;
        row.Status = RequestStatus.Accepted;
        row.AcceptedAt = now;
        row.UpdatedAt = now;
        db.Events.Add(
            RequestViews.StatusEvent(
                row,
                actor!.Value,
                $"Awarded: {winner.Currency} {winner.Amount:0.00}"
            )
        );
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new SendOrgNotice(
                $"Quote accepted: {row.Title}",
                [
                    $"{row.RequesterName} accepted your quote of {winner.Currency} {winner.Amount:0.00} for {row.SiteName}.",
                    "Open the vendor console to start the work.",
                ],
                "marketplace",
                $"Quote accepted: {row.Title}"
            ),
            new DeliveryOptions { TenantId = winner.VendorOrgId.Value.ToString() }
        );
        foreach (var other in losers)
            await bus.PublishAsync(
                new SendOrgNotice(
                    $"Quote not selected: {row.Title}",
                    [$"{row.RequesterName} awarded this request to another vendor."],
                    "marketplace",
                    $"Quote not selected: {row.Title}"
                ),
                new DeliveryOptions { TenantId = other.VendorOrgId.Value.ToString() }
            );
        await MarketplaceAudit.PublishAsync(
            bus,
            actor.Value,
            "marketplace.quote_accepted",
            new
            {
                row.Id,
                QuoteId = quoteId,
                VendorOrgId = winner.VendorOrgId.Value,
                winner.Amount,
            }
        );
        return Results.Ok(RequestViews.Mutated(row));
    }

    /// <summary>Run the org's SLA sweep now (the worker runs it every five minutes).</summary>
    [Transactional(typeof(MarketplaceDbContext))]
    [WolverinePost("/api/marketplace/requests/sla/sweep")]
    [ProducesResponseType(typeof(SlaSweepQueued), StatusCodes.Status200OK)]
    public static async Task<IResult> Sweep(
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        if (!await scopes.CanAsync(accessor.Current, Capabilities.MarketplaceManage, ct))
            return Results.Unauthorized();
        var now = time.GetUtcNow();
        var overdue = await db.Requests.CountAsync(
            r =>
                r.OrgId == actor.Org
                && r.Status == RequestStatus.Submitted
                && r.ResponseDueAt != null
                && r.ResponseDueAt <= now,
            ct
        );
        await bus.PublishAsync(
            new EscalateOverdueRequests(),
            new DeliveryOptions { TenantId = actor.Org.Value.ToString() }
        );
        return Results.Ok(new SlaSweepQueued(now, overdue));
    }

    /// <summary>The requester side of a row: in the reader's org AND site scope, or a 404.</summary>
    private static async Task<(ServiceRequest?, ActorRef?, bool, IResult?)> Load(
        Guid id,
        MarketplaceDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        bool requireManage,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return (null, null, false, Results.Unauthorized());
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.MarketplaceRead, ct);
        var manageScope = await scopes.ScopeForAsync(
            accessor.Current,
            Capabilities.MarketplaceManage,
            ct
        );
        var manage = manageScope is not NodeScope.None;
        if (scope is NodeScope.None && !manage)
            return (null, null, false, Results.Unauthorized());
        var visible = RequestViews.InScope(db.Requests, manage ? manageScope : scope);
        var row = await visible.FirstOrDefaultAsync(r => r.Id == id && r.OrgId == actor.Org, ct);
        if (row is null)
            return (null, null, false, Results.NotFound());
        if (requireManage && !manage)
            return (null, null, false, Results.Unauthorized());
        return (row, actor, manage, null);
    }

    private static async Task<IResult?> Upsell(
        IEntitlements entitlements,
        OrgId org,
        CancellationToken ct
    ) =>
        await entitlements.HasAsync(org, EntitlementCatalog.MarketplaceEnabled, ct)
            ? null
            : Results.Json(
                new
                {
                    error = "the marketplace is not part of this plan",
                    code = EntitlementCatalog.MarketplaceEnabled,
                },
                statusCode: StatusCodes.Status402PaymentRequired
            );

    private static string? Validate(
        string title,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        RequestUrgency urgency,
        string? rrule,
        string? currency,
        decimal? budget
    )
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            return "a request needs a title of up to 200 characters";
        if (endsAt is { } end && end <= startsAt)
            return "the window must end after it starts";
        if (urgency == RequestUrgency.Standing && string.IsNullOrWhiteSpace(rrule))
            return "a standing request needs a recurrence rule";
        if (urgency != RequestUrgency.Standing && !string.IsNullOrWhiteSpace(rrule))
            return "only standing requests carry a recurrence rule";
        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsLetter)))
            return "currency must be a 3-letter ISO code";
        if (budget is < 0)
            return "budget cannot be negative";
        return null;
    }

    private static Dictionary<string, string> CleanSpec(Dictionary<string, string>? spec) =>
        (spec ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s.Key) && !string.IsNullOrWhiteSpace(s.Value))
            .Take(40)
            .ToDictionary(s => s.Key.Trim().ToLowerInvariant(), s => s.Value.Trim());

    internal static string Label(ServiceCategory category) =>
        System
            .Text.RegularExpressions.Regex.Replace(category.ToString(), "([a-z])([A-Z])", "$1 $2")
            .ToLowerInvariant();
}
