using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Storage;

/// <summary>Post-upload pipeline (ADR 19): quarantine-scan, then derivatives for clean files.</summary>
public sealed record ScanUploadedFile(Guid FileId);

public sealed record GenerateDerivatives(Guid FileId);

public static class ScanUploadedFileHandler
{
    [NonTransactional]
    public static async Task Handle(
        ScanUploadedFile message,
        Envelope envelope,
        ITenantContext tenant,
        StorageDbContext db,
        IObjectStore store,
        IVirusScanner scanner,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"ScanUploadedFile arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var file = await db
            .Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == message.FileId, ct);
        if (file is null || file.Status != FileStatus.Uploaded)
            return;

        await using var content = await store.OpenReadAsync(file.Key, ct);
        var verdict = await scanner.ScanAsync(content, ct);
        // Inline transactional completion: a crash before it commits retries the scan;
        // a crash after it commits retries a now-ineligible file without duplicate effects.
        await bus.InvokeForTenantAsync(
            org.Value.ToString(),
            new ApplyFileScan(file.Id, file.Key, verdict),
            ct
        );
    }
}

public static class GenerateDerivativesHandler
{
    private const long PreviewLimit = 1024 * 1024;
    private const int PreviewBytes = 4096;

    [NonTransactional]
    public static async Task Handle(
        GenerateDerivatives message,
        Envelope envelope,
        ITenantContext tenant,
        StorageDbContext db,
        IObjectStore store,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"GenerateDerivatives arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var file = await db
            .Files.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == message.FileId, ct);
        if (file is null || file.Status != FileStatus.Clean || file.PreviewKey != null)
            return;

        // v1 derivative: a text head-preview for small text-ish files. Image
        // thumbnails plug in here (same message, an ImageSharp-based branch).
        var isTextual =
            file.ContentType.StartsWith("text/")
            || file.ContentType is "application/json" or "application/csv";
        if (!isTextual || file.MaxBytes > PreviewLimit)
            return;

        await using var content = await store.OpenReadAsync(file.Key, ct);
        var buffer = new byte[PreviewBytes];
        var read = await content.ReadAtLeastAsync(
            buffer,
            PreviewBytes,
            throwOnEndOfStream: false,
            ct
        );
        await bus.InvokeForTenantAsync(
            org.Value.ToString(),
            new SaveFilePreview(file.Id, file.Key, buffer[..read]),
            ct
        );
    }
}
