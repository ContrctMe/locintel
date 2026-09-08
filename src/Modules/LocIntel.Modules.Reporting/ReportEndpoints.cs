using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Reporting;

public static class ReportEndpoints
{
    public sealed record SubmitRequest(
        string ReportType,
        ReportMode Mode,
        ReportSelection Selection,
        Guid[] SiteIds,
        JsonElement Options
    );

    /// <summary>A site the reader may see, so a run never has to be read as raw ids.</summary>
    public sealed record SiteRef(Guid Id, string Name);

    public sealed record AcceptedResponse(Guid Id, ReportJobState State);

    public sealed record TypeResponse(string Id, string Name, int Version, bool Aggregate);

    public sealed record ItemResponse(
        Guid Id,
        ReportItemState State,
        string? ErrorCode,
        DateTimeOffset? GeneratedAt,
        int Attempt,
        Guid[] SiteIds,
        string[] Warnings
    );

    public sealed record ArtifactResponse(
        Guid Id,
        Guid? ItemId,
        string Name,
        string ContentType,
        long Bytes,
        Guid? FileId
    );

    public sealed record JobResponse(
        Guid Id,
        string ReportType,
        ReportMode Mode,
        ReportSelection Selection,
        ReportJobState State,
        string? ErrorCode,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ExpiresAt,
        ItemResponse[] Items,
        ArtifactResponse[] Artifacts,
        SiteRef[] Sites,
        bool CanModify
    );

    public sealed record DownloadResponse(string Url, int ExpiresInSeconds);

    public sealed record BasemapResponse(string Id, string Attribution);

    [WolverineGet("/api/reports/quota")]
    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [ProducesResponseType(typeof(ReportQuota.Status), 200)]
    public static async Task<IResult> Quota(
        ReportingDbContext db,
        [FromServices] ReportQuota quota,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        return gate is GateOutcome.Allowed { Org: var org }
            ? Results.Ok(await quota.StatusAsync(org, ct))
            : gate.ToResult();
    }

    // Configuration, not tenant data: no DbContext, so no transaction to own.
    [WolverineGet("/api/reports/basemaps")]
    [ProducesResponseType(typeof(BasemapResponse[]), 200)]
    public static async Task<IResult> Basemaps(
        [FromServices] Rendering.ReportBasemaps basemaps,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        return gate is GateOutcome.Allowed
            ? Results.Ok(
                basemaps.All.Select(x => new BasemapResponse(x.Id, x.Attribution)).ToArray()
            )
            : gate.ToResult();
    }

    // The registry is a startup-built list; the same reasoning as Basemaps.
    [WolverineGet("/api/reports/types")]
    [ProducesResponseType(typeof(TypeResponse[]), 200)]
    public static async Task<IResult> Types(
        [FromServices] ReportRegistry registry,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        return gate is GateOutcome.Allowed
            ? Results.Ok(
                registry
                    .All.Select(x => new TypeResponse(x.Id, x.Name, x.Version, x.Aggregate))
                    .ToArray()
            )
            : gate.ToResult();
    }

    [Transactional(typeof(ReportingDbContext))]
    [WolverinePost("/api/reports")]
    [ProducesResponseType(typeof(AcceptedResponse), 200)]
    public static async Task<IResult> Submit(
        SubmitRequest request,
        ReportingDbContext db,
        [FromServices] ReportRegistry registry,
        [FromServices] ReportLimits limits,
        [FromServices] ReportAccess access,
        ISiteSource sites,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        [FromServices] ReportQuota quota,
        IEntitlements entitlements,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        if (!await entitlements.HasAsync(org, EntitlementCatalog.ReportsEnabled, ct))
            return GateResults.FeatureOff(EntitlementCatalog.ReportsEnabled);
        if (!Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.Selection))
            return ApiErrors.BadRequest("Unknown report mode or selection.");
        var definition = registry.Find(request.ReportType);
        if (definition is null || definition.Aggregate != (request.Mode == ReportMode.Aggregate))
            return ApiErrors.BadRequest(
                "Unknown report type, or a mode that report type does not produce."
            );
        if (
            request.Options.ValueKind != JsonValueKind.Object
            || System.Text.Encoding.UTF8.GetByteCount(request.Options.GetRawText()) > 16384
        )
            return ApiErrors.BadRequest("Report options must be an object of at most 16 KiB.");
        if (definition.ValidateOptions(request.Options) is { } error)
            return ApiErrors.BadRequest(error);
        if (!await access.CanGenerateAsync(org, user.UserId, ct))
            return Results.Forbid();
        var limit = request.Mode switch
        {
            ReportMode.Single => 1,
            ReportMode.Aggregate => limits.MaxAggregateSites,
            _ => limits.MaxBatchItems,
        };
        if (
            request.SiteIds is null
            || request.SiteIds.Length > limit
            || request.SiteIds.Any(x => x == Guid.Empty)
            || request.SiteIds.Distinct().Count() != request.SiteIds.Length
        )
            return ApiErrors.BadRequest($"Choose at most {limit} distinct sites.");
        var siteScope = ReportAccess.Intersect(
            await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
            await scopes.ScopeForAsync(user, Capabilities.ReportsGenerate, ct)
        );
        if (
            request.Selection == ReportSelection.Organization
            && siteScope is not NodeScope.EntireOrg
        )
            return Results.Forbid();
        var selected = await sites.SelectAsync(
            org,
            siteScope,
            request.Selection == ReportSelection.Selected ? request.SiteIds : null,
            limit,
            ct
        );
        if (selected.Count == 0 || selected.Count > limit)
            return ApiErrors.BadRequest(
                $"The selection must contain 1..{limit} sites; it has not been truncated."
            );
        if (
            request.Selection == ReportSelection.Selected
            && selected.Count != request.SiteIds.Length
        )
            return Results.NotFound();
        var ids = selected.Select(x => x.Id).ToArray();
        var input = new IReportDefinition.Request(
            org,
            user.UserId,
            ids,
            request.Selection,
            request.Options
        );
        if (!await definition.AuthorizeAsync(input, null, ct))
            return Results.Forbid();
        if (!await db.TryTakeAsync(org.Value, ct))
            return ApiErrors.Busy("Another report is being admitted. Try again in a moment.");
        if (
            await db.Jobs.CountAsync(
                x =>
                    x.OrgId == org
                    && (x.State == ReportJobState.Queued || x.State == ReportJobState.Running),
                ct
            ) >= limits.MaxQueuedJobsPerOrg
        )
            return ApiErrors.Status("The organization already has too many queued reports.", 429);
        var job = new ReportJob
        {
            OrgId = org,
            RequestedBy = user.UserId,
            ReportType = definition.Id,
            DefinitionVersion = definition.Version,
            Mode = request.Mode,
            Selection = request.Selection,
            SiteIds = ids,
            OptionsJson = request.Options.GetRawText(),
        };
        foreach (
            var group in request.Mode == ReportMode.Aggregate
                ? new[] { ids }
                : ids.Select(x => new[] { x })
        )
            job.Items.Add(
                new ReportItem
                {
                    OrgId = org,
                    JobId = job.Id,
                    SiteIds = group,
                }
            );
        EntitlementDecision quotaDecision;
        try
        {
            quotaDecision = await quota.ReserveAsync(
                org,
                job.Id,
                job.Items.Select(x => x.Id).ToArray(),
                ct
            );
        }
        catch (CapacityBusyException)
        {
            return ApiErrors.Busy("Another report is being admitted. Try again in a moment.");
        }
        if (!quotaDecision.IsAllowed)
            return GateResults.LimitReached(quotaDecision);
        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(org, new GenerateReport(job.Id));
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "report.requested",
            new
            {
                job.Id,
                job.ReportType,
                job.Mode,
                count = ids.Length,
            }
        );
        return Results.Ok(new AcceptedResponse(job.Id, job.State));
    }

    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/reports")]
    [ProducesResponseType(typeof(JobResponse[]), 200)]
    public static async Task<IResult> List(
        ReportingDbContext db,
        [FromServices] ReportAccess access,
        [FromServices] ReportRegistry registry,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        var reader = await access.ReaderAsync(org, user.UserId, ct);
        if (reader is null)
            return Results.Ok(Array.Empty<JobResponse>());
        var jobs = await db
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.OrgId == org)
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        // One visibility query for the whole page: resolving the reader and its
        // sites per job turned a 50-run listing into hundreds of queries.
        var names = await access.VisibleSitesAsync(
            org,
            reader,
            jobs.SelectMany(x => x.SiteIds).Distinct().ToArray(),
            ct
        );
        var visible = names.Keys.ToHashSet();
        var result = new List<JobResponse>();
        foreach (var job in jobs)
            if (await access.CanReadJobAsync(job, reader, visible, registry, ct))
                result.Add(View(job, user.UserId, names));
        return Results.Ok(result.ToArray());
    }

    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/reports/{id}")]
    [ProducesResponseType(typeof(JobResponse), 200)]
    public static async Task<IResult> Get(
        Guid id,
        ReportingDbContext db,
        [FromServices] ReportAccess access,
        [FromServices] ReportRegistry registry,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        var job = await db
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.OrgId == org && x.Id == id, ct);
        if (job is null)
            return Results.NotFound();
        var reader = await access.ReaderAsync(org, user.UserId, ct);
        if (reader is null)
            return Results.NotFound();
        var names = await access.VisibleSitesAsync(org, reader, job.SiteIds, ct);
        return await access.CanReadJobAsync(job, reader, names.Keys.ToHashSet(), registry, ct)
            ? Results.Ok(View(job, user.UserId, names))
            : Results.NotFound();
    }

    [Transactional(typeof(ReportingDbContext))]
    [WolverinePost("/api/reports/{id}/cancel")]
    [ProducesResponseType(typeof(AcceptedResponse), 200)]
    public static async Task<IResult> Cancel(
        Guid id,
        ReportingDbContext db,
        [FromServices] ReportLimits limits,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("Report is being modified; retry.");
        var job = await db
            .Jobs.Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.OrgId == org && x.Id == id && x.RequestedBy == user.UserId,
                ct
            );
        if (job is null)
            return Results.NotFound();
        if (job.State is ReportJobState.Queued or ReportJobState.Running)
        {
            job.Revision++;
            Finish(job, ReportJobState.Canceled, limits, time.GetUtcNow());
            await ReportQuota.ReleaseAsync(db, org, job.Id, ct);
            foreach (
                var item in job.Items.Where(x =>
                    x.State is ReportItemState.Queued or ReportItemState.Running
                )
            )
                item.State = ReportItemState.Canceled;
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(new AcceptedResponse(job.Id, job.State));
    }

    [Transactional(typeof(ReportingDbContext))]
    [WolverinePost("/api/reports/{id}/retry")]
    [ProducesResponseType(typeof(AcceptedResponse), 200)]
    public static async Task<IResult> Retry(
        Guid id,
        ReportingDbContext db,
        [FromServices] ReportRegistry registry,
        [FromServices] ReportLimits limits,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        [FromServices] ReportQuota quota,
        IEntitlements entitlements,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ReportsGenerate, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        if (!await entitlements.HasAsync(org, EntitlementCatalog.ReportsEnabled, ct))
            return GateResults.FeatureOff(EntitlementCatalog.ReportsEnabled);
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("Report is being modified; retry.");
        var job = await db
            .Jobs.Include(x => x.Items)
            .FirstOrDefaultAsync(
                x => x.OrgId == org && x.Id == id && x.RequestedBy == user.UserId,
                ct
            );
        if (job is null)
            return Results.NotFound();
        if (
            job.State is not (ReportJobState.Failed or ReportJobState.CompletedWithErrors)
            || job.ExpiresAt <= time.GetUtcNow()
        )
            return ApiErrors.Conflict(
                "Only unexpired failed work can be retried. Generate again for a fresh batch."
            );
        if (registry.Find(job.ReportType, job.DefinitionVersion) is null)
            return ApiErrors.Conflict("Report definition version is unavailable.");
        if (!await db.TryTakeAsync(org.Value, ct))
            return ApiErrors.Busy("Another report is being admitted. Try again in a moment.");
        if (
            await db.Jobs.CountAsync(
                x =>
                    x.OrgId == org
                    && (x.State == ReportJobState.Queued || x.State == ReportJobState.Running),
                ct
            ) >= limits.MaxQueuedJobsPerOrg
        )
            return ApiErrors.Status("Too many queued reports for this organization.", 429);
        EntitlementDecision quotaDecision;
        try
        {
            quotaDecision = await quota.ReserveAsync(
                org,
                job.Id,
                job.Items.Where(x => x.State == ReportItemState.Failed).Select(x => x.Id).ToArray(),
                ct
            );
        }
        catch (CapacityBusyException)
        {
            return ApiErrors.Busy("Another report is being admitted. Try again in a moment.");
        }
        if (!quotaDecision.IsAllowed)
            return GateResults.LimitReached(quotaDecision);
        foreach (var item in job.Items.Where(x => x.State == ReportItemState.Failed))
        {
            item.State = ReportItemState.Queued;
            item.ErrorCode = null;
        }
        job.State = ReportJobState.Queued;
        job.ErrorCode = null;
        job.Revision++;
        job.LeaseOwner = null;
        job.LeaseUntil = null;
        await db.SaveChangesAsync(ct);
        await bus.PublishForOrgAsync(org, new GenerateReport(id));
        return Results.Ok(new AcceptedResponse(id, job.State));
    }

    [Transactional(
        typeof(ReportingDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/reports/{id}/artifacts/{artifactId}/download")]
    [ProducesResponseType(typeof(DownloadResponse), 200)]
    public static async Task<IResult> Download(
        Guid id,
        Guid artifactId,
        ReportingDbContext db,
        [FromServices] ReportRegistry registry,
        [FromServices] ReportAccess access,
        IObjectStore store,
        IReportPublishedFiles files,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        var job = await db
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.OrgId == org && x.Id == id, ct);
        var artifact = job?.Artifacts.SingleOrDefault(x => x.Id == artifactId);
        if (job is null || artifact is null || job.State == ReportJobState.Purging)
            return Results.NotFound();
        // A generated PDF becomes an ordinary site file (ADR 56). Storage owns its
        // authorization, trash, holds and TTL, so it is served from
        // /api/files/{fileId}/download and never signed a second time here. The run
        // hands clients that id as ArtifactResponse.FileId. This route serves the
        // run's own bundle. 404 rather than a redirect: one route, one response shape.
        if (artifact.ItemId is not null || !artifact.Ready || artifact.Revision != job.Revision)
            return Results.NotFound();
        if (
            job.ExpiresAt <= time.GetUtcNow()
            || job.ExpiresAt is null
                && job.State is not (ReportJobState.Queued or ReportJobState.Running)
        )
            return Results.NotFound();
        var definition = registry.Find(job.ReportType, job.DefinitionVersion);
        if (definition is null)
            return Results.Forbid();
        var reader = await access.ReaderAsync(org, user.UserId, ct);
        if (reader is null)
            return Results.Forbid();
        var items = job.Items.Where(x => x.State == ReportItemState.Succeeded).ToArray();
        if (items.Length == 0)
            return Results.NotFound();
        foreach (var item in items)
            if (
                !await access.CanReadSitesAsync(org, reader, item.SiteIds, ct)
                || !await definition.AuthorizeAsync(
                    ReportAccess.Request(job, item.SiteIds) with
                    {
                        UserId = user.UserId,
                    },
                    JsonSerializer.Deserialize<JsonElement>(item.DependenciesJson),
                    ct
                )
            )
                return Results.Forbid();
        // A cached ZIP cannot bypass trash, erasure or changed source permissions.
        var pdfs = job
            .Artifacts.Where(x => x.Ready && x.ItemId != null)
            .Select(x => x.Id)
            .ToArray();
        if (!await files.AreCleanAsync(org, pdfs, ct))
            return Results.NotFound();
        if (!await access.CanReadSitesAsync(org, reader, job.SiteIds, ct))
            return Results.Forbid();
        var ttl = job.ExpiresAt is { } expires
            ? Math.Min(60, (int)(expires - time.GetUtcNow()).TotalSeconds)
            : 60;
        if (ttl < 1)
            return Results.NotFound();
        return Results.Ok(
            new DownloadResponse(
                (
                    await store.GetDownloadUrlAsync(artifact.Key, TimeSpan.FromSeconds(ttl), ct)
                ).ToString(),
                ttl
            )
        );
    }

    internal static void Finish(
        ReportJob job,
        ReportJobState state,
        ReportLimits limits,
        DateTimeOffset now
    )
    {
        job.State = state;
        job.CompletedAt = now;
        job.ExpiresAt ??= job.CompletedAt.Value.AddDays(limits.ArtifactDays);
        job.MetadataExpiresAt ??= job.CompletedAt.Value.AddDays(
            Math.Max(limits.ArtifactDays, limits.MetadataDays)
        );
    }

    private static JobResponse View(
        ReportJob job,
        Guid userId,
        IReadOnlyDictionary<Guid, string> names
    ) =>
        new(
            job.Id,
            job.ReportType,
            job.Mode,
            job.Selection,
            job.State,
            job.ErrorCode,
            job.CreatedAt,
            job.ExpiresAt,
            job.Items.Select(x => new ItemResponse(
                    x.Id,
                    x.State,
                    x.ErrorCode,
                    x.GeneratedAt,
                    x.Attempt,
                    x.SiteIds,
                    JsonSerializer.Deserialize<string[]>(x.WarningsJson) ?? []
                ))
                .ToArray(),
            job.Artifacts.Where(x => x.Ready && (x.ItemId != null || x.Revision == job.Revision))
                .Select(x => new ArtifactResponse(
                    x.Id,
                    x.ItemId,
                    x.Name,
                    x.ContentType,
                    x.Bytes,
                    x.FilePublished ? x.Id : null
                ))
                .ToArray(),
            job.SiteIds.Where(names.ContainsKey).Select(x => new SiteRef(x, names[x])).ToArray(),
            job.RequestedBy == userId
        );
}
