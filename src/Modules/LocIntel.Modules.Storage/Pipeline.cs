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
    [Transactional(typeof(StorageDbContext))]
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

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == message.FileId, ct);
        if (file is null || file.Status != FileStatus.Uploaded)
            return;

        await using var content = await store.OpenReadAsync(file.Key, ct);
        var verdict = await scanner.ScanAsync(content, ct);
        file.Status = verdict == ScanVerdict.Clean ? FileStatus.Clean : FileStatus.Quarantined;
        file.ScannedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        if (file.Status == FileStatus.Clean)
            await bus.PublishAsync(
                new GenerateDerivatives(file.Id),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        else
            await bus.PublishAsync(
                new LocIntel.Contracts.RecordDomainAudit(
                    "file.quarantined",
                    System.Text.Json.JsonSerializer.Serialize(new { file.Id, file.Name })
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
    }
}

public static class GenerateDerivativesHandler
{
    private const long PreviewLimit = 1024 * 1024;
    private const int PreviewBytes = 4096;

    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        GenerateDerivatives message,
        Envelope envelope,
        ITenantContext tenant,
        StorageDbContext db,
        IObjectStore store,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is null)
            throw new InvalidOperationException(
                $"GenerateDerivatives arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == message.FileId, ct);
        if (file is null || file.Status != FileStatus.Clean)
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
        var previewKey = file.Key + ".preview.txt";
        await store.WriteAsync(previewKey, new MemoryStream(buffer, 0, read), "text/plain", ct);
        file.PreviewKey = previewKey;
        await db.SaveChangesAsync(ct);
    }
}
