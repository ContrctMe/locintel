using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Storage;

public sealed record FileSummary(
    Guid Id,
    string Name,
    string ContentType,
    string Status,
    DateTimeOffset? DeletedAt,
    bool LegalHold,
    bool HasPreview,
    DateTimeOffset CreatedAt,
    Guid[] SiteIds,
    string? Origin,
    Guid? OriginId
);

public sealed record FileListResponse(
    IReadOnlyList<FileSummary> Items,
    int? Total,
    int? NextOffset
);

public sealed record CreateFileRequest(
    string Name,
    string ContentType,
    long SizeBytes,
    Guid? SiteId = null
);

public sealed record SetHoldRequest(bool Hold);

public sealed record DownloadFileResponse(string Url, int ExpiresInSeconds);

public sealed record CreateFileResponse(Guid FileId, UploadTicket Ticket);

public static class FileEndpoints
{
    private const long MaxUploadBytes = 100 * 1024 * 1024;

    [Transactional(typeof(StorageDbContext))]
    [WolverinePost("/api/files")]
    [ProducesResponseType(typeof(CreateFileResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateFileRequest request,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        if (request.SizeBytes is <= 0 or > MaxUploadBytes)
            return ApiErrors.BadRequest($"size must be 1..{MaxUploadBytes} bytes");

        if (
            string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Length > 300
            || string.IsNullOrWhiteSpace(request.ContentType)
            || request.ContentType.Length > 100
        )
            return ApiErrors.BadRequest("invalid file name or content type");
        var id = Guid.CreateVersion7();
        // tenant- and region-scoped key layout (ADR 19/35)
        var key = $"{RegionId.Default.Value}/{org.Value}/files/{id}";
        var file = new FileObject
        {
            Id = id,
            OrgId = org,
            Key = key,
            Name = request.Name,
            ContentType = request.ContentType,
            MaxBytes = request.SizeBytes,
            CreatedBy = userId,
            SiteIds = request.SiteId is { } siteId ? [siteId] : [],
        };
        if (
            request.SiteId is not null
            && !await access.AllowsAsync(file, principal, Capabilities.FilesManage, ct)
        )
            return Results.NotFound();
        if (!await CapacityReservations.TryLockAsync(db, org, "storage.upload", ct))
            return ApiErrors.Conflict("another upload is being admitted; retry");
        var outstanding = db.Files.Where(f =>
            f.Status == FileStatus.PendingUpload
            || f.Status == FileStatus.Uploaded
            || f.Status == FileStatus.Quarantined
        );
        if (
            await outstanding.CountAsync(ct) >= 20
            || (await outstanding.SumAsync(f => (long?)f.MaxBytes, ct) ?? 0) + request.SizeBytes
                > 500L * 1024 * 1024
        )
            return ApiErrors.Status(
                "outstanding upload budget exceeded",
                StatusCodes.Status429TooManyRequests
            );
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);

        var ticket = store.SupportsBoundedUpload
            ? await store.CreateUploadTicketAsync(key, request.ContentType, request.SizeBytes, ct)
            : new UploadTicket(
                $"/api/files/{id}/content",
                "PUT",
                new Dictionary<string, string> { ["Content-Type"] = request.ContentType },
                file.CreatedAt.AddMinutes(15)
            );
        return Results.Ok(new CreateFileResponse(id, ticket));
    }

    /// <summary>Client signals the direct upload finished; scanning starts (quarantine until verdict).</summary>
    [Transactional(typeof(StorageDbContext))]
    [WolverinePost("/api/files/{id}/complete")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public static async Task<IResult> Complete(
        Guid id,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IObjectStore store,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        await db.TakeAsync(id, ct);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
            return Results.NotFound();
        if (!await access.AllowsAsync(file, accessor.Current, Capabilities.FilesManage, ct))
            return Results.NotFound();
        if (file.Status != FileStatus.PendingUpload)
            return ApiErrors.Conflict($"file is {file.Status}");
        var length = await store.GetLengthAsync(file.Key, ct);
        if (length is null or 0)
            return ApiErrors.BadRequest("no bytes were uploaded for this ticket");
        if (length > file.MaxBytes)
        {
            // Retain the inaccessible object until the ticket expires, then sweep it.
            // Deleting now would let the still-live create-only ticket upload again.
            file.Status = FileStatus.Quarantined;
            await db.SaveChangesAsync(ct);
            return ApiErrors.Status(
                $"uploaded object exceeds the declared {file.MaxBytes} bytes",
                StatusCodes.Status413PayloadTooLarge
            );
        }

        file.Status = FileStatus.Uploaded;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ScanUploadedFile(file.Id),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.Accepted();
    }

    [Transactional(
        typeof(StorageDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/files")]
    [ProducesResponseType(typeof(FileListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        StorageDbContext db,
        [FromServices] FileAccess access,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string? q,
        int? limit,
        int? offset,
        bool? trash,
        Guid? siteId,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesRead, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesRead).ToResult();
        var query = trash is true
            ? db.Files.Where(f => f.Status == FileStatus.Deleted)
            : db.Files.Where(f =>
                f.Status != FileStatus.Erased
                && f.Status != FileStatus.Deleted
                && f.DeletedAt == null
            );
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(f => EF.Functions.ILike(f.Name, $"%{q.Trim()}%"));
        if (siteId is { } selectedSite)
            query = query.Where(f => f.SiteIds.Contains(selectedSite));
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        // Bound permission checks to one candidate page; a global visible count would
        // require scanning every producer's dependency policy. Total is intentionally unknown.
        var candidates = await query
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(ct);
        var files = new List<FileSummary>();
        foreach (var file in candidates.Take(take))
            if (await access.AllowsAsync(file, accessor.Current, Capabilities.FilesRead, ct))
                files.Add(View(file));
        return Results.Ok(
            new FileListResponse(files, null, candidates.Count > take ? skip + take : null)
        );
    }

    /// <summary>Authorization happens HERE, before signing - the URL itself is unguarded (ADR 19).</summary>
    [Transactional(
        typeof(StorageDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/files/{id}/download")]
    [ProducesResponseType(typeof(DownloadFileResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Download(
        Guid id,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesRead, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesRead).ToResult();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        // quarantined/pending/erased are all 404: never confirm undownloadable bytes
        if (file is null || file.Status != FileStatus.Clean)
            return Results.NotFound();
        if (!await access.AllowsAsync(file, accessor.Current, Capabilities.FilesRead, ct))
            return Results.NotFound();
        var url = await store.GetDownloadUrlAsync(file.Key, TimeSpan.FromMinutes(5), ct);
        return Results.Ok(new DownloadFileResponse(url.ToString(), 300));
    }

    [Transactional(typeof(StorageDbContext))]
    [WolverinePost("/api/files/{id}/hold")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> SetHold(
        Guid id,
        SetHoldRequest request,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        await db.TakeAsync(id, ct);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
            return Results.NotFound();
        if (!await access.AllowsAsync(file, accessor.Current, Capabilities.FilesManage, ct))
            return Results.NotFound();
        file.LegalHold = request.Hold;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new LocIntel.Contracts.RecordDomainAudit(
                request.Hold ? "file.hold_placed" : "file.hold_released",
                System.Text.Json.JsonSerializer.Serialize(new { file.Id, file.Name })
            ),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.NoContent();
    }

    /// <summary>
    /// Tier-2 deletion, as ADR 25 promises: into the TRASH with bytes
    /// retained, restorable until the window closes (Storage:TrashRetentionDays,
    /// default 30). The sweep erases bytes after that; the row stays as a
    /// tombstone either way. Legal hold blocks even the trash.
    /// </summary>
    [Transactional(typeof(StorageDbContext))]
    [WolverineDelete("/api/files/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Delete(
        Guid id,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        await db.TakeAsync(id, ct);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || file.Status is FileStatus.Erased or FileStatus.Deleted)
            return Results.NotFound();
        if (!await access.AllowsAsync(file, accessor.Current, Capabilities.FilesManage, ct))
            return Results.NotFound();
        if (file.LegalHold)
            return ApiErrors.Conflict("file is under legal hold");

        // Only Clean content earns a restore window. Fresh direct cloud tickets
        // retain inaccessible bytes until expiry so deletion cannot revive them.
        if (
            file.Status != FileStatus.Clean
            && store.SupportsBoundedUpload
            && store is not LocalObjectStore
            && file.CreatedAt.AddMinutes(15) > DateTimeOffset.UtcNow
        )
        {
            // Keep the create-only cloud ticket occupied and charged to admission
            // until expiry; deleting its object now would permit a fresh upload.
            file.Status = FileStatus.Quarantined;
            file.DeletedAt = DateTimeOffset.UtcNow;
        }
        else if (file.Status != FileStatus.Clean)
        {
            await FileBytes.EraseAsync(file, store, ct);
            file.Status = FileStatus.Erased;
            file.PreviewKey = null;
        }
        else
        {
            file.Status = FileStatus.Deleted;
            file.DeletedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new LocIntel.Contracts.RecordDomainAudit(
                file.Status != FileStatus.Erased ? "file.deleted" : "file.erased",
                System.Text.Json.JsonSerializer.Serialize(new { file.Id, file.Name })
            ),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.NoContent();
    }

    [Transactional(typeof(StorageDbContext))]
    [WolverinePost("/api/files/{id}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Restore(
        Guid id,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        await db.TakeAsync(id, ct);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || file.Status != FileStatus.Deleted)
            return Results.NotFound();

        if (!await access.AllowsAsync(file, accessor.Current, Capabilities.FilesManage, ct))
            return Results.NotFound();
        file.Status = FileStatus.Clean; // only Clean files can enter the trash
        file.DeletedAt = null;
        await db.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new LocIntel.Contracts.RecordDomainAudit(
                "file.restored",
                System.Text.Json.JsonSerializer.Serialize(new { file.Id, file.Name })
            ),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.NoContent();
    }

    internal static FileSummary View(FileObject file) =>
        new(
            file.Id,
            file.Name,
            file.ContentType,
            file.Status.ToString(),
            file.DeletedAt,
            file.LegalHold,
            file.PreviewKey != null,
            file.CreatedAt,
            file.SiteIds,
            file.Origin,
            file.OriginId
        );
}
