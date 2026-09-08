using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
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
                f.Id,
                f.Name,
                f.ContentType,
                f.SiteIds,
                f.Origin,
                f.OriginId,
                status = f.Status.ToString(),
                f.LegalHold,
                f.CreatedAt,
            })
            .ToBoundedExportListAsync(ct);
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

        await db.TakeAsync(org.Value, ct);
        if (await db.PurgedOrganizations.AnyAsync(x => x.OrgId == org, ct))
        {
            if (message.AdmissionId is { } cancelled)
                await CapacityReservations.ConsumeAsync(
                    db,
                    org,
                    ExportAdmission.Code,
                    cancelled,
                    ct
                );
            return;
        }

        var admission =
            message.AdmissionId
            ?? await ExportAdmission.TryReserveAsync(db, org, ct)
            ?? throw new CapacityBusyException();
        if (!await db.TryTakeAsync(admission, ct))
            throw new CapacityBusyException();
        if (!await ExportAdmission.ExistsAsync(db, org, admission, ct))
            return; // Already completed or deliberately cancelled.

        using var buffer = new MemoryStream();
        try
        {
            long bytes = 0;
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var exporter in exporters.OrderBy(e => e.Section))
            {
                var entry = zip.CreateEntry($"{exporter.Section}.json", CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                var json = await exporter.ExportJsonAsync(org, ct);
                bytes = ExportLimits.AddSection(bytes, json);
                await entryStream.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
            }
        }
        catch (InvalidDataException)
        {
            await bus.AuditAsync(
                org,
                AuditActor.User(message.RequestedBy),
                "org.export_failed",
                new { reason = "export_budget_exceeded" }
            );
            await CapacityReservations.ConsumeAsync(db, org, ExportAdmission.Code, admission, ct);
            return;
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
                Origin = "org-export",
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
            "org.exported",
            new { fileId = id, sections = exporters.Select(e => e.Section).Order() }
        );
        await CapacityReservations.ConsumeAsync(db, org, ExportAdmission.Code, admission, ct);
    }
}

/// <summary>
/// Envelope-tenanted purge of the org's files: bytes and derivatives leave
/// storage, rows leave the table. Legal hold does not survive the org - the
/// operator two-step (suspend, then offboard) is the deliberate control here.
/// </summary>
public static class PurgeOrgFilesHandler
{
    [Transactional(typeof(StorageDbContext))]
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
        await LocIntel.Platform.Data.AggregateLock.TakeAsync(db, org.Value, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform.capacity_reservations WHERE org_id = {org.Value} AND code = {ExportAdmission.Code}",
            ct
        );
        if (!await db.PurgedOrganizations.AnyAsync(x => x.OrgId == org, ct))
            db.PurgedOrganizations.Add(new PurgedFileOrganization { OrgId = org });
        await db.SaveChangesAsync(ct);
        var files = await db
            .Files.IgnoreQueryFilters()
            .Where(f => f.OrgId == org)
            .OrderBy(f => f.Id)
            .ToListAsync(ct);
        foreach (var file in files)
        {
            await db.TakeAsync(file.Id, ct);
            await db.Entry(file).ReloadAsync(ct);
            await FileBytes.EraseAsync(file, store, ct);
        }
        await db.Files.IgnoreQueryFilters().Where(f => f.OrgId == org).ExecuteDeleteAsync(ct);
    }
}
