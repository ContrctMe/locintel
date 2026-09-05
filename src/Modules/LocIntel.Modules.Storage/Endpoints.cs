using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
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
    DateTimeOffset CreatedAt
);

public sealed record FileListResponse(IReadOnlyList<FileSummary> Items, int Total, int? NextOffset);

public sealed record CreateFileRequest(string Name, string ContentType, long SizeBytes);

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
            return Results.BadRequest(new { error = $"size must be 1..{MaxUploadBytes} bytes" });

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
        };
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);

        var ticket = await store.CreateUploadTicketAsync(
            key,
            request.ContentType,
            request.SizeBytes,
            ct
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
        IObjectStore store,
        IMessageBus bus,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
            return Results.NotFound();
        if (file.Status != FileStatus.PendingUpload)
            return Results.Conflict(new { error = $"file is {file.Status}" });
        var length = await store.GetLengthAsync(file.Key, ct);
        if (length is null or 0)
            return Results.BadRequest(new { error = "no bytes were uploaded for this ticket" });
        if (length > file.MaxBytes)
            return Results.Json(
                new { error = $"uploaded object exceeds the declared {file.MaxBytes} bytes" },
                statusCode: StatusCodes.Status413PayloadTooLarge
            );

        file.Status = FileStatus.Uploaded;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ScanUploadedFile(file.Id),
            new DeliveryOptions { TenantId = file.OrgId.Value.ToString() }
        );
        return Results.Accepted();
    }

    [Transactional(typeof(StorageDbContext))]
    [WolverineGet("/api/files")]
    [ProducesResponseType(typeof(FileListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        StorageDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        string? q,
        int? limit,
        int? offset,
        bool? trash,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesRead, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesRead).ToResult();
        var query = trash is true
            ? db.Files.Where(f => f.Status == FileStatus.Deleted)
            : db.Files.Where(f => f.Status != FileStatus.Erased && f.Status != FileStatus.Deleted);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(f => EF.Functions.ILike(f.Name, $"%{q.Trim()}%"));
        var total = await query.CountAsync(ct);
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var skip = Math.Max(offset ?? 0, 0);
        var files = await query
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .Skip(skip)
            .Take(take)
            .Select(f => new FileSummary(
                f.Id,
                f.Name,
                f.ContentType,
                f.Status.ToString(),
                f.DeletedAt,
                f.LegalHold,
                f.PreviewKey != null,
                f.CreatedAt
            ))
            .ToListAsync(ct);
        return Results.Ok(
            new FileListResponse(
                files,
                total,
                skip + files.Count < total ? skip + files.Count : null
            )
        );
    }

    /// <summary>Authorization happens HERE, before signing - the URL itself is unguarded (ADR 19).</summary>
    [Transactional(typeof(StorageDbContext))]
    [WolverineGet("/api/files/{id}/download")]
    [ProducesResponseType(typeof(DownloadFileResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Download(
        Guid id,
        StorageDbContext db,
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
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
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
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || file.Status is FileStatus.Erased or FileStatus.Deleted)
            return Results.NotFound();
        if (file.LegalHold)
            return Results.Conflict(new { error = "file is under legal hold" });

        // only CLEAN content earns the restore window; anything else
        // (quarantined, never-scanned) erases immediately - a trash
        // round-trip must never launder a quarantined file back to Clean
        if (file.Status != FileStatus.Clean)
        {
            await store.DeleteAsync(file.Key, ct);
            if (file.PreviewKey is { } previewKey)
                await store.DeleteAsync(previewKey, ct);
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
                file.Status == FileStatus.Deleted ? "file.deleted" : "file.erased",
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
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!await scopes.CanAsync(accessor.Current, Capabilities.FilesManage, ct))
            return new GateOutcome.Forbidden(Capabilities.FilesManage).ToResult();
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || file.Status != FileStatus.Deleted)
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
}
