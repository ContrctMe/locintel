using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Http;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Secrets;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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

public sealed record ImportCounts(int Create, int Update, int Close, int Unchanged, int Invalid);

public sealed record StagedUploadResponse(Guid BatchId, ImportCounts Counts);

public sealed record StagedSiteResponse(
    string ExternalId,
    string Name,
    string NodePath,
    string Action,
    string[] Errors,
    string[] Changes
);

public sealed record IngestPreviewResponse(
    Guid Id,
    string Source,
    string Status,
    ImportCounts Counts,
    IReadOnlyList<StagedSiteResponse> Rows
);

public sealed record ImportBatchResponse(
    Guid Id,
    string Source,
    string Status,
    ImportCounts Counts,
    DateTimeOffset CreatedAt
);

public sealed record CommitBatchResponse(int Applied);

public sealed record ConnectorResponse(
    Guid Id,
    string Name,
    string Type,
    string Url,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSyncedAt,
    int? SyncIntervalHours
);

public sealed record ConnectorCreatedResponse(Guid Id);

public static class IngestEndpoints
{
    /// <summary>Stage a CLEAN uploaded CSV: parse, validate, diff - nothing applied.</summary>
    [NonTransactional]
    [WolverinePost("/api/ingest/uploads")]
    [ProducesResponseType(typeof(StagedUploadResponse), StatusCodes.Status200OK)]
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
        var gate = await IngestAccess.RequireUserAsync(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;

        var file = await files.GetAsync(request.FileId, ct);
        if (file is null)
            return Results.NotFound();
        if (file.Status != "Clean")
            return ApiErrors.Conflict($"file is {file.Status}; only Clean files can be staged");

        ImportBatch batch;
        try
        {
            await using var stream = await store.OpenReadAsync(file.Key, ct);
            using var reader = new StreamReader(
                new MemoryStream(await BoundedRead.ReadAsync(stream, IngestLimits.MaxBytes, ct))
            );
            var text = await reader.ReadToEndAsync(ct);
            var rows = CsvParser.Parse(text).Select(CsvParser.ToSourceRow).ToList();
            if (rows.Count == 0)
                return ApiErrors.BadRequest("no data rows found");
            // Object storage is finished before the staging lock and database transaction begin.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            batch = await staging.StageAsync(org, userId, "upload", rows, ct);
            await transaction.CommitAsync(ct);
        }
        catch (InvalidDataException e)
        {
            return ApiErrors.BadRequest(e.Message);
        }
        return Results.Ok(new StagedUploadResponse(batch.Id, Counts(batch.Counts)));
    }

    /// <summary>The diff preview (ADR 18): what WOULD happen, row by row.</summary>
    [Transactional(
        typeof(IngestDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/ingest/batches/{id}")]
    [ProducesResponseType(typeof(IngestPreviewResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Preview(
        Guid id,
        IngestDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await IngestAccess.CanAsync(accessor.Current, scopes, ct))
            return new GateOutcome.Forbidden(Capabilities.IngestManage).ToResult();
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
    [Transactional(
        typeof(IngestDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/ingest/batches")]
    [ProducesResponseType(typeof(List<ImportBatchResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> ListBatches(
        IngestDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await IngestAccess.CanAsync(accessor.Current, scopes, ct))
            return new GateOutcome.Forbidden(Capabilities.IngestManage).ToResult();
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
            batches
                .Select(b => new ImportBatchResponse(
                    b.Id,
                    b.Source,
                    b.status,
                    Counts(b.counts),
                    b.CreatedAt
                ))
                .ToList()
        );
    }

    /// <summary>
    /// Discard a staged batch: the diff is thrown away, nothing was applied.
    /// The batch row stays as the record (with its counts); the staged rows -
    /// bulk working data - are deleted (tier 3).
    /// </summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/ingest/batches/{id}/discard")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Discard(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await IngestAccess.RequireUserAsync(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("batch is being modified; retry the request");
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        if (batch.Status != BatchStatus.Staged)
            return ApiErrors.Conflict($"batch is {batch.Status}");

        batch.Status = BatchStatus.Discarded;
        await db.StagedSites.Where(s => s.BatchId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(userId),
            "ingest.batch_discarded",
            new { batchId = batch.Id }
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
    [ProducesResponseType(typeof(CommitBatchResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Commit(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteLookup sites,
        IEntitlements entitlements,
        CancellationToken ct
    )
    {
        var gate = await IngestAccess.RequireUserAsync(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("batch is being modified; retry the request");
        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (batch is null)
            return Results.NotFound();
        if (batch.Status != BatchStatus.Staged)
            return ApiErrors.Conflict($"batch is {batch.Status}");

        var actionable = await db
            .StagedSites.Where(s =>
                s.BatchId == id
                && (s.Action == "create" || s.Action == "update" || s.Action == "close")
            )
            .ToListAsync(ct);
        // gate 1 once for the whole batch: the creates it holds against the
        // plan's ceiling (a limit failure is 402-and-upsell, never an error)
        var creates = actionable.Count(r => r.Action == "create");
        if (creates > 0)
        {
            if (!await CapacityReservations.TryLockAsync(db, org, EntitlementCatalog.MaxSites, ct))
                return ApiErrors.Status(
                    "site capacity is busy; retry the request",
                    StatusCodes.Status503ServiceUnavailable
                );
            var occupied =
                await sites.CountSitesAsync(ct)
                + await CapacityReservations.PendingAsync(db, org, EntitlementCatalog.MaxSites, ct);
            var decision = await entitlements.CheckLimitAsync(
                org,
                EntitlementCatalog.MaxSites,
                occupied,
                creates,
                ct
            );
            if (!decision.IsAllowed)
                return GateResults.LimitReached(decision);
            await CapacityReservations.ReserveAsync(
                db,
                org,
                EntitlementCatalog.MaxSites,
                id,
                actionable.Where(r => r.Action == "create").Select(r => r.Id).ToArray(),
                ct
            );
        }
        foreach (var row in actionable)
            await bus.PublishAsync(
                new SiteChangeRequested(
                    row.Action,
                    row.ExternalId,
                    row.Name,
                    row.TimeZone,
                    row.NodeId,
                    row.Action == "create" ? row.Id : null
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );

        batch.Status = BatchStatus.Committed;
        await db.SaveChangesAsync(ct);
        // was published with no actor headers at all: the trail recorded that a
        // batch was committed but never who committed it
        await bus.AuditAsync(
            org,
            AuditActor.User(userId),
            "ingest.batch_committed",
            new { batchId = batch.Id, applied = actionable.Count }
        );
        return Results.Ok(new CommitBatchResponse(actionable.Count));
    }

    // ---- connectors (ADR 18/31) ----

    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/connectors")]
    [ProducesResponseType(typeof(ConnectorCreatedResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> CreateConnector(
        CreateConnectorRequest request,
        IngestDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IHostEnvironment environment,
        CancellationToken ct
    )
    {
        var gate = await IngestAccess.RequireUserAsync(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();

        if (
            request.Name is null
            || request.Name.Trim().Length is 0 or > 120
            || !PublicHttp.IsAllowedUrl(
                request.Url,
                environment.IsDevelopment() || environment.IsEnvironment("Testing")
            )
            || request.SyncIntervalHours is <= 0 or > 8760
            || request.ApiKey is { Length: > 4096 }
        )
            return ApiErrors.BadRequest(
                "Use a name up to 120 characters, a public HTTPS URL, and a positive sync interval up to one year."
            );
        if (!await db.TryTakeAsync(org.Value, ct))
            return ApiErrors.Conflict("Connector configuration is busy.");
        if (await db.Connectors.CountAsync(ct) >= IngestLimits.MaxConnectors)
            return ApiErrors.Status(
                "Connector limit reached.",
                StatusCodes.Status429TooManyRequests
            );
        var connector = new SiteConnector
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Name = request.Name.Trim(),
            Type = "json-http",
            Url = request.Url,
            // ADR 31: envelope-encrypted, never plaintext at rest
            EncryptedCredentials = await EnvelopeCrypto.EncryptAsync(request.ApiKey, kms, ct),
        };
        connector.SyncIntervalHours = request.SyncIntervalHours;
        db.Connectors.Add(connector);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new ConnectorCreatedResponse(connector.Id));
    }

    /// <summary>Connector inventory - credentials never leave the envelope.</summary>
    [Transactional(
        typeof(IngestDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/connectors")]
    [ProducesResponseType(typeof(List<ConnectorResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> ListConnectors(
        IngestDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await IngestAccess.CanAsync(accessor.Current, scopes, ct))
            return new GateOutcome.Forbidden(Capabilities.IngestManage).ToResult();
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

    private static ImportCounts Counts(string json)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, int>>(json)!;
        return new ImportCounts(
            values["create"],
            values["update"],
            values["close"],
            values["unchanged"],
            values["invalid"]
        );
    }

    /// <summary>Edit a connector; the key only rewraps when a new one is provided.</summary>
    [Transactional(typeof(IngestDbContext))]
    [WolverinePut("/api/connectors/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> UpdateConnector(
        Guid id,
        UpdateConnectorRequest request,
        IngestDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IHostEnvironment environment,
        CancellationToken ct
    )
    {
        if (!await IngestAccess.CanAsync(accessor.Current, scopes, ct))
            return new GateOutcome.Forbidden(Capabilities.IngestManage).ToResult();
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("Connector is syncing; retry the request.");
        var connector = await db.Connectors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connector is null)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Url))
            return ApiErrors.BadRequest("name and url are required");

        if (
            request.Name is null
            || request.Name.Trim().Length is 0 or > 120
            || !PublicHttp.IsAllowedUrl(
                request.Url,
                environment.IsDevelopment() || environment.IsEnvironment("Testing")
            )
            || request.SyncIntervalHours is <= 0 or > 8760
            || request.ApiKey is { Length: > 4096 }
        )
            return ApiErrors.BadRequest(
                "Use a name up to 120 characters, a public HTTPS URL, and a positive sync interval up to one year."
            );
        var sameOrigin =
            Uri.TryCreate(connector.Url, UriKind.Absolute, out var previousUrl)
            && Uri.Compare(
                previousUrl,
                new Uri(request.Url),
                UriComponents.SchemeAndServer,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase
            ) == 0;
        if (!sameOrigin && string.IsNullOrWhiteSpace(request.ApiKey))
            return ApiErrors.BadRequest(
                "changing the connector origin requires replacement credentials"
            );
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> DeleteConnector(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await IngestAccess.RequireUserAsync(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        if (!await db.TryTakeAsync(id, ct))
            return ApiErrors.Conflict("Connector is syncing; retry the request.");
        var connector = await db.Connectors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (connector is null)
            return Results.NotFound();
        await CapacityReservations.ConsumeAsync(db, org, ConnectorQueue.Code, id, ct);
        db.Connectors.Remove(connector);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(userId),
            "connector.deleted",
            new { connectorId = id, connector.Name }
        );
        return Results.NoContent();
    }

    [Transactional(typeof(IngestDbContext))]
    [WolverinePost("/api/connectors/{id}/sync")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public static async Task<IResult> Sync(
        Guid id,
        IngestDbContext db,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await IngestAccess.RequireUserAsync(accessor, scopes, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        if (!await db.Connectors.AnyAsync(c => c.Id == id, ct))
            return Results.NotFound();
        if (!await ConnectorQueue.EnqueueAsync(db, org, id, bus, ct))
            return ApiErrors.Status(
                "A sync is already pending or the organization queue is full.",
                StatusCodes.Status429TooManyRequests
            );
        return Results.Accepted();
    }
}
