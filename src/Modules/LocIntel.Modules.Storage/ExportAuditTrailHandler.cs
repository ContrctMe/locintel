using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Storage;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Storage;

/// <summary>
/// Assembles the audit-trail archive (mirror of ExportOrgDataHandler): one
/// JSONL file per kind plus a manifest that says whether any kind was
/// truncated, landing as a regular Clean file in the org's own library.
/// </summary>
public static class ExportAuditTrailHandler
{
    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        ExportAuditTrail message,
        StorageDbContext db,
        IAuditTrailExporter exporter,
        IObjectStore store,
        ITenantContext tenant,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException(
                "audit export arrived with no tenant on the envelope"
            );

        var sections = await exporter.ExportAsync(org, ct);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var section in sections)
            {
                var entry = zip.CreateEntry($"{section.Kind}.jsonl", CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(section.Jsonl), ct);
            }
            var manifest = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var manifestStream = manifest.Open();
            await manifestStream.WriteAsync(
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        exportedAt = DateTimeOffset.UtcNow,
                        kinds = sections.Select(s => new
                        {
                            kind = s.Kind,
                            lines = s.Jsonl.Count(c => c == '\n'),
                            s.Truncated,
                        }),
                    },
                    JsonSerializerOptions.Web
                ),
                ct
            );
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
                Name = $"audit-export-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.zip",
                ContentType = "application/zip",
                MaxBytes = buffer.Length,
                // internally generated, never touched an upload ticket: born Clean
                Status = FileStatus.Clean,
                CreatedBy = message.RequestedBy,
                ScannedAt = DateTimeOffset.UtcNow,
            }
        );

        await bus.AuditAsync(
            org,
            AuditActor.User(message.RequestedBy),
            "audit.exported",
            new
            {
                fileId = id,
                truncatedKinds = sections.Where(s => s.Truncated).Select(s => s.Kind),
            }
        );
    }
}
