using LocIntel.Contracts;
using LocIntel.Contracts.Incidents;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents.Api;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// The incident fact table's surface. Three gates: incidents:read filters
/// the list by scope (gate 3 never fails, it filters); incidents:report
/// files and annotates within scope; incidents:manage edits, closes, holds,
/// and trashes. Every id-addressed miss is a 404 - never a 403.
/// </summary>
public static class IncidentEndpoints
{
    [Transactional(typeof(IncidentsDbContext))]
    [WolverineGet("/api/incidents")]
    [ProducesResponseType(typeof(IncidentListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Guid? siteId,
        IncidentStatus? status,
        IncidentCategory? category,
        IncidentSeverity? severity,
        DateOnly? from,
        DateOnly? to,
        string? q,
        bool? trash,
        int? limit,
        int? offset,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        IQueryable<Incident> query = db.Incidents;
        if (trash is true)
        {
            if (!await scopes.CanAsync(accessor.Current, Capabilities.IncidentsManage, ct))
                return Results.Unauthorized();
            query = query
                .IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
                .Where(i => i.DeletedAt != null);
        }
        query = query.InScope(scope);
        if (siteId is { } site)
            query = query.Where(i => i.SiteId == site);
        if (status is { } s)
            query = query.Where(i => i.Status == s);
        if (category is { } c)
            query = query.Where(i => i.Category == c);
        if (severity is { } sev)
            query = query.Where(i => i.Severity == sev);
        if (from is { } f)
            query = query.Where(i => i.BusinessDate >= f);
        if (to is { } t)
            query = query.Where(i => i.BusinessDate <= t);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = $"%{q.Trim()}%";
            query = query.Where(i =>
                EF.Functions.ILike(i.Title, needle) || EF.Functions.ILike(i.Narrative, needle)
            );
        }
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var items = await query
            .OrderByDescending(i => i.OccurredAt)
            .ThenByDescending(i => i.Id)
            .Skip(skip)
            .Take(take)
            .Select(i => new IncidentSummary(
                i.Id,
                i.SiteId,
                i.Category,
                i.Severity,
                i.Status,
                i.Title,
                i.OccurredAt,
                i.BusinessDate,
                i.LossAmount,
                i.LegalHold,
                i.DeletedAt
            ))
            .ToListAsync(ct);
        return Results.Ok(
            new IncidentListResponse(
                items,
                total,
                skip + items.Count < total ? skip + items.Count : null
            )
        );
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverineGet("/api/incidents/{id}")]
    [ProducesResponseType(typeof(IncidentDetail), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        IStoredFileLookup files,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsRead, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        // the trash is visible to managers (restore screen); the SoftDelete
        // filter is the only one disabled - Tenant stays on, RLS backstops
        var manage = await scopes.CanAsync(accessor.Current, Capabilities.IncidentsManage, ct);
        var source = manage
            ? db.Incidents.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
            : db.Incidents;
        var incident = await source.InScope(scope).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null)
            return Results.NotFound();

        var notes = await db
            .Notes.Where(n => n.IncidentId == id)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(ct);
        var attachments = await db
            .Attachments.Where(a => a.IncidentId == id)
            .OrderBy(a => a.AddedAt)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            notes.Select(n => n.AuthorId).Append(incident.ReportedBy).Distinct().ToList(),
            ct
        );
        var attachmentViews = new List<IncidentAttachmentView>(attachments.Count);
        foreach (var attachment in attachments)
        {
            var file = await files.GetAsync(attachment.FileId, ct);
            attachmentViews.Add(
                new IncidentAttachmentView(
                    attachment.Id,
                    attachment.FileId,
                    file?.Name,
                    file?.ContentType,
                    file?.Status,
                    attachment.Label,
                    attachment.AddedAt
                )
            );
        }
        return Results.Ok(
            new IncidentDetail(
                incident.Id,
                incident.SiteId,
                incident.Path.ToString(),
                incident.Category,
                incident.Severity,
                incident.Status,
                incident.Source,
                incident.Title,
                incident.Narrative,
                incident.LocationDetail,
                incident.OccurredAt,
                incident.BusinessDate,
                incident.ReportedAt,
                incident.ReportedBy,
                labels.GetValueOrDefault(incident.ReportedBy),
                incident.ReporterContact,
                incident.LossAmount,
                incident.RecoveredAmount,
                incident.Currency,
                incident.PoliceReportNumber,
                incident.Tags,
                incident.ClosedAt,
                incident.ClosureReason,
                incident.LegalHold,
                incident.UpdatedAt,
                incident.DeletedAt,
                notes
                    .Select(n => new IncidentNoteView(
                        n.Id,
                        n.AuthorId,
                        labels.GetValueOrDefault(n.AuthorId),
                        n.Body,
                        n.CreatedAt
                    ))
                    .ToList(),
                attachmentViews
            )
        );
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents")]
    [ProducesResponseType(typeof(IncidentCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Report(
        ReportIncidentRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsReport, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var site = await sites.FindAsync(request.SiteId, ct);
        if (site is null || !scope.Covers(site.Path))
            return Results.NotFound();
        if (
            Validate(
                request.Title,
                request.Currency,
                request.LossAmount,
                request.RecoveredAmount
            ) is
            { } error
        )
            return Results.BadRequest(new { error });

        var incident = new Incident
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            SiteId = site.Id,
            HierarchyId = site.HierarchyId,
            Path = new LTree(site.Path),
            Category = request.Category,
            Severity = request.Severity,
            Source = actor.IsService ? IncidentSource.Api : IncidentSource.Console,
            Title = request.Title.Trim(),
            Narrative = request.Narrative?.Trim() ?? "",
            LocationDetail = Clean(request.LocationDetail),
            OccurredAt = request.OccurredAt.ToUniversalTime(),
            BusinessDate = BusinessDate.For(request.OccurredAt, site.TimeZone),
            ReportedBy = actor.Id,
            LossAmount = request.LossAmount,
            RecoveredAmount = request.RecoveredAmount,
            Currency = (request.Currency ?? "USD").ToUpperInvariant(),
            PoliceReportNumber = Clean(request.PoliceReportNumber),
            Tags = CleanTags(request.Tags),
        };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(
            bus,
            actor,
            "incident.reported",
            new
            {
                incident.Id,
                incident.SiteId,
                Category = incident.Category.ToString(),
                Severity = incident.Severity.ToString(),
                incident.BusinessDate,
            }
        );
        await bus.PublishAsync(
            new IncidentReported(
                incident.Id,
                incident.SiteId,
                site.Name,
                incident.Path.ToString(),
                incident.Category.ToString(),
                incident.Severity.ToString(),
                incident.Title,
                incident.OccurredAt,
                incident.ReportedBy
            ),
            new DeliveryOptions { TenantId = actor.Org.Value.ToString() }
        );
        return Results.Ok(new IncidentCreated(incident.Id, incident.BusinessDate));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePut("/api/incidents/{id}")]
    [ProducesResponseType(typeof(IncidentMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateIncidentRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (incident, actor, error) = await LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsManage,
            ct
        );
        if (error is not null)
            return error;
        if (
            Validate(
                request.Title,
                request.Currency,
                request.LossAmount,
                request.RecoveredAmount
            ) is
            { } invalid
        )
            return Results.BadRequest(new { error = invalid });

        incident!.Category = request.Category;
        incident.Severity = request.Severity;
        incident.Title = request.Title.Trim();
        incident.Narrative = request.Narrative?.Trim() ?? "";
        incident.LocationDetail = Clean(request.LocationDetail);
        if (incident.OccurredAt != request.OccurredAt.ToUniversalTime())
        {
            // the stamp follows the instant; the site's zone is the authority
            var site = await sites.FindAsync(incident.SiteId, ct);
            incident.OccurredAt = request.OccurredAt.ToUniversalTime();
            if (site is not null)
                incident.BusinessDate = BusinessDate.For(incident.OccurredAt, site.TimeZone);
        }
        incident.LossAmount = request.LossAmount;
        incident.RecoveredAmount = request.RecoveredAmount;
        incident.Currency = (request.Currency ?? incident.Currency).ToUpperInvariant();
        incident.PoliceReportNumber = Clean(request.PoliceReportNumber);
        incident.Tags = CleanTags(request.Tags);
        incident.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(
            bus,
            actor!.Value,
            "incident.updated",
            new { incident.Id }
        );
        return Results.Ok(Mutated(incident));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/{id}/close")]
    [ProducesResponseType(typeof(IncidentMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Close(
        Guid id,
        CloseIncidentRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (incident, actor, error) = await LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsManage,
            ct
        );
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { error = "closing an incident needs a reason" });
        if (incident!.Status == IncidentStatus.Closed)
            return Results.Conflict(new { error = "already closed" });
        incident.Status = IncidentStatus.Closed;
        incident.ClosedAt = DateTimeOffset.UtcNow;
        incident.ClosedBy = actor!.Value.Id;
        incident.ClosureReason = request.Reason.Trim();
        incident.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(
            bus,
            actor.Value,
            "incident.closed",
            new { incident.Id, incident.ClosureReason }
        );
        return Results.Ok(Mutated(incident));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/{id}/reopen")]
    [ProducesResponseType(typeof(IncidentMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Reopen(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (incident, actor, error) = await LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsManage,
            ct
        );
        if (error is not null)
            return error;
        if (incident!.Status == IncidentStatus.Open)
            return Results.Conflict(new { error = "already open" });
        incident.Status = IncidentStatus.Open;
        incident.ClosedAt = null;
        incident.ClosedBy = null;
        incident.ClosureReason = null;
        incident.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(
            bus,
            actor!.Value,
            "incident.reopened",
            new { incident.Id }
        );
        return Results.Ok(Mutated(incident));
    }

    /// <summary>Legal hold: the record is evidence - deletion refuses while it is set.</summary>
    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/{id}/hold")]
    [ProducesResponseType(typeof(IncidentMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> SetHold(
        Guid id,
        SetIncidentHoldRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (incident, actor, error) = await LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsManage,
            ct
        );
        if (error is not null)
            return error;
        incident!.LegalHold = request.Hold;
        incident.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(
            bus,
            actor!.Value,
            request.Hold ? "incident.hold_set" : "incident.hold_cleared",
            new { incident.Id }
        );
        return Results.Ok(Mutated(incident));
    }

    /// <summary>Tier 2 (ADR 25): into the trash, restorable; refused under legal hold.</summary>
    [Transactional(typeof(IncidentsDbContext))]
    [WolverineDelete("/api/incidents/{id}")]
    [ProducesResponseType(typeof(IncidentMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Delete(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (incident, actor, error) = await LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsManage,
            ct
        );
        if (error is not null)
            return error;
        if (incident!.LegalHold)
            return Results.Conflict(new { error = "incident is under legal hold" });
        incident.DeletedAt = DateTimeOffset.UtcNow;
        incident.UpdatedAt = incident.DeletedAt.Value;
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(
            bus,
            actor!.Value,
            "incident.deleted",
            new { incident.Id }
        );
        return Results.Ok(Mutated(incident));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/{id}/restore")]
    [ProducesResponseType(typeof(IncidentMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Restore(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsManage, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var incident = await db
            .Incidents.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
            .InScope(scope)
            .FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt != null, ct);
        if (incident is null)
            return Results.NotFound();
        incident.DeletedAt = null;
        incident.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(bus, actor, "incident.restored", new { incident.Id });
        return Results.Ok(Mutated(incident));
    }

    /// <summary>Gate 2 then gate 3 for a write: (incident, actor) or the response to return instead.</summary>
    internal static async Task<(Incident?, ActorRef?, IResult?)> LoadForWrite(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string capability,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return (null, null, Results.Unauthorized());
        var scope = await scopes.ScopeForAsync(accessor.Current, capability, ct);
        if (scope is NodeScope.None)
            return (null, null, Results.Unauthorized());
        var incident = await db.Incidents.InScope(scope).FirstOrDefaultAsync(i => i.Id == id, ct);
        return incident is null ? (null, null, Results.NotFound()) : (incident, actor, null);
    }

    private static IncidentMutated Mutated(Incident incident) =>
        new(
            incident.Id,
            incident.Status,
            incident.LegalHold,
            incident.BusinessDate,
            incident.DeletedAt,
            incident.UpdatedAt
        );

    private static string? Validate(
        string title,
        string? currency,
        decimal? loss,
        decimal? recovered
    )
    {
        if (string.IsNullOrWhiteSpace(title))
            return "an incident needs a title";
        if (title.Trim().Length > 200)
            return "title is limited to 200 characters";
        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsLetter)))
            return "currency must be a 3-letter ISO code";
        if (loss is < 0 || recovered is < 0)
            return "amounts cannot be negative";
        return null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string[] CleanTags(string[]? tags) =>
        tags is null
            ? []
            : tags.Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct()
                .Take(20)
                .ToArray();
}
