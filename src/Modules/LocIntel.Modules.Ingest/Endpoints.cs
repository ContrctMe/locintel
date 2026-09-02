using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Secrets;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Ingest;

public sealed record StageUploadRequest(Guid FileId);

public sealed record CreateConnectorRequest(
    string Name,
    string Url,
    string ApiKey,
    int? SyncIntervalHours = null
);

public sealed record UpdateConnectorRequest(
    string Name,
    string Url,
    string? ApiKey = null,
    int? SyncIntervalHours = null
);

public static class IngestEndpoints
{
    /// <summary>Stage a CLEAN uploaded CSV: parse, validate, diff - nothing applied.</summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/ingest/uploads")]
    public static async Task<IResult> StageUpload(
        StageUploadRequest request,
        IngestDbContext db,
        StagingService staging,
        IStoredFileLookup files,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.IngestManage, ct)
        )
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

        var rows = LocIntel
            .Platform.Text.CsvParser.Parse(text)
            .Select(SourceRows.FromRecord)
            .ToList();
        if (rows.Count == 0)
            return Results.BadRequest(new { error = "no data rows found" });

        var batch = await staging.StageAsync(org, userId, "upload", rows, ct);
        return Results.Ok(
            new { batchId = batch.Id, counts = JsonSerializer.Deserialize<object>(batch.Counts) }
        );
    }

    /// <summary>The diff preview (ADR 18): what WOULD happen, row by row.</summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverineGet("/api/ingest/batches/{id}")]
    public static async Task<IResult> Preview(
        Guid id,
        IngestDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.IngestManage, ct))
            return Results.Unauthorized();
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        var rows = await db
            .StagedSites.Where(s => s.BatchId == id)
            .Select(s => new
            {
                s.ExternalId,
                s.Name,
                s.NodePath,
                s.Action,
                errors = s.Errors,
                changes = s.Changes,
            })
            .ToListAsync(ct);
        return Results.Ok(
            new
            {
                batch.Id,
                batch.Source,
                status = batch.Status.ToString(),
                counts = JsonSerializer.Deserialize<object>(batch.Counts),
                rows,
            }
        );
    }

    /// <summary>Recent batches, newest first: the ingest history at a glance.</summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverineGet("/api/ingest/batches")]
    public static async Task<IResult> ListBatches(
        IngestDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.IngestManage, ct))
            return Results.Unauthorized();
        var batches = await db
            .Batches.OrderByDescending(b => b.CreatedAt)
            .Take(50)
            .Select(b => new
            {
                b.Id,
                b.Source,
                status = b.Status.ToString(),
                counts = b.Counts,
                b.CreatedAt,
            })
            .ToListAsync(ct);
        return Results.Ok(
            batches.Select(b => new
            {
                b.Id,
                b.Source,
                b.status,
                counts = JsonSerializer.Deserialize<object>(b.counts),
                b.CreatedAt,
            })
        );
    }

    /// <summary>
    /// Discard a staged batch: the diff is thrown away, nothing was applied.
    /// The batch row stays as the record (with its counts); the staged rows -
    /// bulk working data - are deleted (tier 3).
    /// </summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/ingest/batches/{id}/discard")]
    public static async Task<IResult> Discard(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.IngestManage, ct)
        )
            return Results.Unauthorized();
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        if (batch.Status != BatchStatus.Staged)
            return Results.Conflict(new { error = $"batch is {batch.Status}" });

        batch.Status = BatchStatus.Discarded;
        await db.StagedSites.Where(s => s.BatchId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new RecordDomainAudit(
                "ingest.batch_discarded",
                JsonSerializer.Serialize(new { batchId = batch.Id })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = userId.ToString(),
                },
            }
        );
        return Results.NoContent();
    }

    /// <summary>
    /// Commit: publish one SiteChangeRequested per actionable row over the
    /// outbox - Tenancy applies them (ADR 17). Invalid/unchanged rows are
    /// skipped; the batch commits exactly once.
    /// </summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/ingest/batches/{id}/commit")]
    public static async Task<IResult> Commit(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.IngestManage, ct)
        )
            return Results.Unauthorized();
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        if (batch.Status != BatchStatus.Staged)
            return Results.Conflict(new { error = $"batch is {batch.Status}" });

        var actionable = await db
            .StagedSites.Where(s =>
                s.BatchId == id
                && (s.Action == "create" || s.Action == "update" || s.Action == "close")
            )
            .ToListAsync(ct);
        foreach (var row in actionable)
            await bus.PublishAsync(
                new SiteChangeRequested(
                    row.Action,
                    row.ExternalId,
                    row.Name,
                    row.TimeZone,
                    row.NodeId
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );

        batch.Status = BatchStatus.Committed;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new RecordDomainAudit(
                "ingest.batch_committed",
                JsonSerializer.Serialize(new { batchId = batch.Id, applied = actionable.Count })
            ),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return Results.Ok(new { applied = actionable.Count });
    }

    // ---- connectors (ADR 18/31) ----

    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/connectors")]
    public static async Task<IResult> CreateConnector(
        CreateConnectorRequest request,
        IngestDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.IngestManage, ct)
        )
            return Results.Unauthorized();

        var connector = new SiteConnector
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Name = request.Name,
            Type = "json-http",
            Url = request.Url,
            // ADR 31: envelope-encrypted, never plaintext at rest
            EncryptedCredentials = await EnvelopeCrypto.EncryptAsync(request.ApiKey, kms, ct),
        };
        connector.SyncIntervalHours = request.SyncIntervalHours;
        db.Connectors.Add(connector);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { connector.Id });
    }

    /// <summary>Connector inventory - credentials never leave the envelope.</summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverineGet("/api/connectors")]
    public static async Task<IResult> ListConnectors(
        IngestDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.IngestManage, ct))
            return Results.Unauthorized();
        var connectors = await db
            .Connectors.OrderBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Type,
                c.Url,
                c.CreatedAt,
                c.LastSyncedAt,
                c.SyncIntervalHours,
            })
            .ToListAsync(ct);
        return Results.Ok(connectors);
    }

    /// <summary>Edit a connector; the key only rewraps when a new one is provided.</summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverinePut("/api/connectors/{id}")]
    public static async Task<IResult> UpdateConnector(
        Guid id,
        UpdateConnectorRequest request,
        IngestDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.IngestManage, ct))
            return Results.Unauthorized();
        var connector = await db.Connectors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connector is null)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Url))
            return Results.BadRequest(new { error = "name and url are required" });

        connector.Name = request.Name.Trim();
        connector.Url = request.Url.Trim();
        connector.SyncIntervalHours = request.SyncIntervalHours;
        if (!string.IsNullOrEmpty(request.ApiKey))
            connector.EncryptedCredentials = await EnvelopeCrypto.EncryptAsync(
                request.ApiKey,
                kms,
                ct
            );
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Tier 3: connectors are configuration, hard-deleted. The audit trail keeps the fact.</summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverineDelete("/api/connectors/{id}")]
    public static async Task<IResult> DeleteConnector(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.IngestManage, ct)
        )
            return Results.Unauthorized();
        var connector = await db.Connectors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connector is null)
            return Results.NotFound();
        db.Connectors.Remove(connector);
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new RecordDomainAudit(
                "connector.deleted",
                JsonSerializer.Serialize(new { connectorId = id, connector.Name })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = userId.ToString(),
                },
            }
        );
        return Results.NoContent();
    }

    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/connectors/{id}/sync")]
    public static async Task<IResult> Sync(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org } principal
            || !await scopes.CanAsync(principal, Capabilities.IngestManage, ct)
        )
            return Results.Unauthorized();
        if (!await db.Connectors.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();
        await bus.PublishAsync(
            new SyncSiteConnector(id),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return Results.Accepted();
    }
}
