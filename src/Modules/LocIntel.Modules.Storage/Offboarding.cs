using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Storage;

/// <summary>Storage's slice of the offboarding export: what files existed (metadata; the bytes stay in storage).</summary>
public sealed class StorageExporter(StorageDbContext db) : IOrgDataExporter
{
    public string Section => "storage";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var files = await db
            .Files.IgnoreQueryFilters()
            .Where(f => f.OrgId == org)
            .Select(f => new
            {
                f.Name,
                f.ContentType,
                status = f.Status.ToString(),
                f.LegalHold,
                f.CreatedAt,
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new { files },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }
        );
    }
}

/// <summary>
/// Assembles the offboarding archive: every module's exporter contributes a
/// section, the zip lands as a regular Clean file in the org's own library, and
/// the existing download flow (authz, presigned URL) serves it. Runs
/// envelope-tenanted; the exporters' reads and the FileObject row all live
/// under the org's RLS session.
/// </summary>
public static class ExportOrgDataHandler
{
    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        ExportOrgData message,
        StorageDbContext db,
        IEnumerable<IOrgDataExporter> exporters,
        IObjectStore store,
        ITenantContext tenant,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("export arrived with no tenant on the envelope");

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var exporter in exporters.OrderBy(e => e.Section))
            {
                var entry = zip.CreateEntry($"{exporter.Section}.json", CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                var json = await exporter.ExportJsonAsync(org, ct);
                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
            }
        }
        buffer.Position = 0;

        var id = Guid.CreateVersion7();
        var key = $"{RegionId.Default.Value}/{org.Value}/files/{id}";
        await store.WriteAsync(key, buffer, "application/zip", ct);
        db.Files.Add(
            new FileObject
            {
                Id = id,
                OrgId = org,
                Key = key,
                Name = $"org-export-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.zip",
                ContentType = "application/zip",
                MaxBytes = buffer.Length,
                // internally generated, never touched an upload ticket: born Clean
                Status = FileStatus.Clean,
                CreatedBy = message.RequestedBy,
                ScannedAt = DateTimeOffset.UtcNow,
            }
        );

        await bus.PublishAsync(
            new RecordDomainAudit(
                "org.exported",
                JsonSerializer.Serialize(
                    new { fileId = id, sections = exporters.Select(e => e.Section).Order() }
                )
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = message.RequestedBy.ToString(),
                },
            }
        );
    }
}

/// <summary>
/// Envelope-tenanted purge of the org's files: bytes and derivatives leave
/// storage, rows leave the table. Legal hold does not survive the org - the
/// operator two-step (suspend, then offboard) is the deliberate control here.
/// </summary>
public static class PurgeOrgFilesHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgFiles _,
        StorageDbContext db,
        IObjectStore store,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        var files = await db
            .Files.IgnoreQueryFilters()
            .Where(f => f.OrgId == org)
            .Select(f => new { f.Key, f.PreviewKey })
            .ToListAsync(ct);
        foreach (var file in files)
        {
            await store.DeleteAsync(file.Key, ct);
            if (file.PreviewKey is { } preview)
                await store.DeleteAsync(preview, ct);
        }
        await db.Files.IgnoreQueryFilters().Where(f => f.OrgId == org).ExecuteDeleteAsync(ct);
    }
}
