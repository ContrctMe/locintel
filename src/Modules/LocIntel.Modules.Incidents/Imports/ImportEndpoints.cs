using System.Globalization;
using LocIntel.Contracts;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Imports.Api;
using LocIntel.Modules.Incidents.Incidents;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using LocIntel.Platform.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Incidents.Imports;

/// <summary>
/// Historical incidents from a CSV (onboarding a tenant's back catalog).
/// Stage parses, resolves sites by external id or name, validates, and
/// applies gate 3 to every row; commit lands the valid rows as incidents
/// with Source = Import (no alerts - history is not news). incidents:manage.
/// </summary>
public static class ImportEndpoints
{
    public const int MaxRows = 5000;

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/imports")]
    [ProducesResponseType(typeof(ImportBatchView), StatusCodes.Status200OK)]
    public static async Task<IResult> Stage(
        StageImportRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IStoredFileLookup files,
        IObjectStore store,
        ISiteLookup siteLookup,
        ISiteDirectory sites,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsManage, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var file = await files.GetAsync(request.FileId, ct);
        if (file is null)
            return Results.NotFound();
        if (file.Status != "Clean")
            return Results.Conflict(
                new { error = $"file is {file.Status}; only Clean files can be staged" }
            );
        string text;
        await using (var stream = await store.OpenReadAsync(file.Key, ct))
        using (var reader = new StreamReader(stream))
            text = await reader.ReadToEndAsync(ct);
        var records = CsvParser.Parse(text);
        if (records.Count == 0)
            return Results.BadRequest(new { error = "no data rows found" });
        if (records.Count > MaxRows)
            return Results.BadRequest(
                new { error = $"imports are limited to {MaxRows} rows per file" }
            );

        // site resolution: external id first, then name, both case-insensitive;
        // then gate 3 - a site outside the importer's scope is an invalid row
        var snapshots = await siteLookup.ListSitesAsync(ct);
        var byExternal = snapshots
            .Where(s => s.ExternalId is not null)
            .GroupBy(s => s.ExternalId!.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var byName = snapshots
            .GroupBy(s => s.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var covered = new Dictionary<Guid, bool>();
        var batch = new IncidentImportBatch
        {
            Id = Guid.CreateVersion7(),
            OrgId = actor.Org,
            FileId = file.Id,
            FileName = file.Name,
            CreatedBy = actor.Id,
        };
        var rowNumber = 1;
        foreach (var record in records)
        {
            rowNumber++;
            var errors = new List<string>();
            var siteRef = record.GetValueOrDefault("site", "").Trim();
            Guid? siteId = null;
            if (siteRef.Length == 0)
                errors.Add("site is required");
            else if (byExternal.TryGetValue(siteRef.ToLowerInvariant(), out var s1))
                siteId = s1.Id.Value;
            else if (byName.TryGetValue(siteRef.ToLowerInvariant(), out var s2))
                siteId = s2.Id.Value;
            else
                errors.Add($"unknown site '{siteRef}'");
            if (siteId is { } sid)
            {
                if (!covered.TryGetValue(sid, out var ok))
                {
                    var info = await sites.FindAsync(sid, ct);
                    ok = info is not null && scope.Covers(info.Path);
                    covered[sid] = ok;
                }
                if (!ok)
                    errors.Add("site is outside your scope");
            }
            IncidentCategory? category = Enum.TryParse<IncidentCategory>(
                record.GetValueOrDefault("category", "").Replace(" ", ""),
                true,
                out var c
            )
                ? c
                : null;
            if (category is null)
                errors.Add("unknown category");
            IncidentSeverity? severity = Enum.TryParse<IncidentSeverity>(
                record.GetValueOrDefault("severity", "Medium").Trim(),
                true,
                out var sv
            )
                ? sv
                : null;
            if (severity is null)
                errors.Add("unknown severity");
            DateTimeOffset? occurredAt = DateTimeOffset.TryParse(
                record.GetValueOrDefault("occurred_at", ""),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var at
            )
                ? at.ToUniversalTime()
                : null;
            if (occurredAt is null)
                errors.Add("occurred_at is not a date");
            var title = record.GetValueOrDefault("title", "").Trim();
            if (title.Length == 0)
                errors.Add("title is required");
            if (title.Length > 200)
                errors.Add("title is limited to 200 characters");
            decimal? loss = null;
            var lossText = record.GetValueOrDefault("loss_amount", "").Trim();
            if (lossText.Length > 0)
            {
                if (
                    decimal.TryParse(
                        lossText,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var l
                    )
                    && l >= 0
                )
                    loss = l;
                else
                    errors.Add("loss_amount is not a non-negative number");
            }
            db.ImportRows.Add(
                new IncidentImportRow
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = actor.Org,
                    BatchId = batch.Id,
                    RowNumber = rowNumber,
                    SiteRef = siteRef,
                    SiteId = siteId,
                    Category = category,
                    Severity = severity,
                    OccurredAt = occurredAt,
                    Title = title.Length > 200 ? title[..200] : title,
                    Narrative = record.GetValueOrDefault("narrative", "").Trim(),
                    LossAmount = loss,
                    PoliceReportNumber = Clean(record.GetValueOrDefault("police_report", "")),
                    Tags = record
                        .GetValueOrDefault("tags", "")
                        .Split(
                            ';',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                        )
                        .Select(t => t.ToLowerInvariant())
                        .Distinct()
                        .Take(20)
                        .ToArray(),
                    Errors = errors.ToArray(),
                }
            );
            batch.Total++;
            if (errors.Count == 0)
                batch.Valid++;
            else
                batch.Invalid++;
        }
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        var labels = await actors.LabelsAsync([actor.Id], ct);
        return Results.Ok(View(batch, labels));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverineGet("/api/incidents/imports")]
    [ProducesResponseType(typeof(ImportBatchListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.IncidentsManage, ct))
            return Results.Unauthorized();
        var batches = await db
            .ImportBatches.OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync(
            batches.Select(b => b.CreatedBy).Distinct().ToList(),
            ct
        );
        return Results.Ok(
            new ImportBatchListResponse(batches.Select(b => View(b, labels)).ToList())
        );
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverineGet("/api/incidents/imports/{id}")]
    [ProducesResponseType(typeof(ImportBatchDetail), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.IncidentsManage, ct))
            return Results.Unauthorized();
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        var rows = await db
            .ImportRows.Where(r => r.BatchId == id)
            .OrderBy(r => r.RowNumber)
            .ToListAsync(ct);
        var labels = await actors.LabelsAsync([batch.CreatedBy], ct);
        return Results.Ok(
            new ImportBatchDetail(
                View(batch, labels),
                rows.Select(r => new ImportRowView(
                        r.RowNumber,
                        r.SiteRef,
                        r.SiteId,
                        r.Category,
                        r.Severity,
                        r.OccurredAt,
                        r.Title,
                        r.LossAmount,
                        r.Errors,
                        r.IncidentId
                    ))
                    .ToList()
            )
        );
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/imports/{id}/commit")]
    [ProducesResponseType(typeof(ImportCommitted), StatusCodes.Status200OK)]
    public static async Task<IResult> Commit(
        Guid id,
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
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsManage, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        if (batch.Status != ImportStatus.Staged)
            return Results.Conflict(new { error = $"batch is {batch.Status}" });
        var rows = await db
            .ImportRows.Where(r => r.BatchId == id && r.Errors.Length == 0)
            .ToListAsync(ct);
        var siteInfo = new Dictionary<Guid, SiteInfo?>();
        var created = 0;
        foreach (var row in rows)
        {
            if (!siteInfo.TryGetValue(row.SiteId!.Value, out var site))
                siteInfo[row.SiteId.Value] = site = await sites.FindAsync(row.SiteId.Value, ct);
            if (site is null || !scope.Covers(site.Path))
                continue; // moved or re-scoped since staging: skip, never guess
            var occurredAt = row.OccurredAt!.Value;
            var clock = BusinessDate.LocalClock(occurredAt, site.TimeZone);
            var incident = new Incident
            {
                Id = Guid.CreateVersion7(),
                OrgId = actor.Org,
                SiteId = site.Id,
                HierarchyId = site.HierarchyId,
                Path = new LTree(site.Path),
                Category = row.Category!.Value,
                Severity = row.Severity!.Value,
                Source = IncidentSource.Import,
                Title = row.Title,
                Narrative = row.Narrative,
                OccurredAt = occurredAt,
                BusinessDate = BusinessDate.For(occurredAt, site.TimeZone),
                LocalHour = clock.Hour,
                LocalWeekday = clock.Weekday,
                ReportedBy = actor.Id,
                LossAmount = row.LossAmount,
                PoliceReportNumber = row.PoliceReportNumber,
                Tags = row.Tags,
            };
            db.Incidents.Add(incident);
            row.IncidentId = incident.Id;
            created++;
        }
        batch.Status = ImportStatus.Committed;
        batch.CommittedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await IncidentAudit.PublishAsync(
            bus,
            actor,
            "incident.import_committed",
            new
            {
                BatchId = id,
                batch.FileName,
                Created = created,
            }
        );
        return Results.Ok(new ImportCommitted(id, created));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/imports/{id}/discard")]
    [ProducesResponseType(typeof(ImportBatchMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> Discard(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.IncidentsManage, ct))
            return Results.Unauthorized();
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        if (batch.Status != ImportStatus.Staged)
            return Results.Conflict(new { error = $"batch is {batch.Status}" });
        batch.Status = ImportStatus.Discarded;
        await db.ImportRows.Where(r => r.BatchId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ImportBatchMutated(id, batch.Status));
    }

    private static ImportBatchView View(
        IncidentImportBatch b,
        IReadOnlyDictionary<Guid, string> labels
    ) =>
        new(
            b.Id,
            b.FileId,
            b.FileName,
            b.Status,
            b.Total,
            b.Valid,
            b.Invalid,
            b.CreatedBy,
            labels.GetValueOrDefault(b.CreatedBy),
            b.CreatedAt,
            b.CommittedAt
        );

    private static string? Clean(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
