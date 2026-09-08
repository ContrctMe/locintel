using LocIntel.Contracts;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Ingest;

public sealed record SourceRow(
    string ExternalId,
    string Name,
    string TimeZone,
    string NodePath,
    string Status
);

/// <summary>
/// The shared staging core (ADR 18): file uploads and pull connectors both
/// land here. Rows are validated and diffed against live sites BY EXTERNAL ID
/// - the mapping that makes re-runs idempotent - and nothing is applied until
/// an explicit commit.
/// </summary>
public sealed class StagingService(IngestDbContext db, ISiteLookup sites)
{
    public async Task<ImportBatch> StageAsync(
        OrgId org,
        Guid createdBy,
        string source,
        IReadOnlyList<SourceRow> rows,
        CancellationToken ct,
        Guid? batchId = null
    )
    {
        if (
            rows.Count > IngestLimits.MaxRows
            || rows.Any(r =>
                r.ExternalId.Length > 120
                || r.Name.Length > 200
                || r.TimeZone.Length > 64
                || r.NodePath.Length > 500
                || r.Status.Length > 10
            )
        )
            throw new InvalidDataException("Import exceeds row or field limits.");
        await db.TakeAsync(org.Value, ct);
        // Retain bounded working history; committed/discarded rows are ephemera, not audit evidence.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await db
            .StagedSites.Where(r =>
                db.Batches.Any(b =>
                    b.Id == r.BatchId && b.Status != BatchStatus.Staged && b.CreatedAt < cutoff
                )
            )
            .ExecuteDeleteAsync(ct);
        if (await db.StagedSites.CountAsync(ct) + rows.Count > IngestLimits.MaxStagedRows)
            throw new InvalidDataException(
                "Organization staging limit reached; discard unused batches."
            );
        // the file's ids, not the org's sites: the diff reads what it needs (code review, 2026-09)
        var externalIds = rows.Select(r => r.ExternalId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToArray();
        var liveSites = (await sites.ListSitesAsync(externalIds, ct))
            .Where(s => s.ExternalId is not null)
            .GroupBy(s => s.ExternalId!)
            .ToDictionary(g => g.Key, g => g.First());
        var nodes = await sites.ListNodesAsync(ct);
        var nodesByPath = nodes
            .GroupBy(n => n.NamePath)
            .ToDictionary(g => g.Key, g => g.First().Id);
        // a spreadsheet may carry the node's id instead of its name path
        var nodeIds = nodes.Select(n => n.Id).ToHashSet();

        var batch = new ImportBatch
        {
            Id = batchId ?? Guid.CreateVersion7(),
            OrgId = org,
            Source = source,
            CreatedBy = createdBy,
        };
        var counts = new Dictionary<string, int>
        {
            ["create"] = 0,
            ["update"] = 0,
            ["close"] = 0,
            ["unchanged"] = 0,
            ["invalid"] = 0,
        };
        var seen = new HashSet<string>();

        foreach (var row in rows)
        {
            var staged = new StagedSite
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                BatchId = batch.Id,
                ExternalId = row.ExternalId,
                Name = row.Name,
                TimeZone = row.TimeZone,
                NodePath = row.NodePath,
                SourceStatus = row.Status,
                Action = "invalid",
            };
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(row.ExternalId))
                errors.Add("external_id is required");
            else if (!seen.Add(row.ExternalId))
                errors.Add($"duplicate external_id '{row.ExternalId}' in this file");
            if (string.IsNullOrWhiteSpace(row.Name))
                errors.Add("name is required");
            if (!BusinessDate.IsValidTimeZone(row.TimeZone))
                errors.Add($"'{row.TimeZone}' is not an IANA time zone");
            if (row.Status is not ("open" or "closed"))
                errors.Add($"status must be open|closed, got '{row.Status}'");
            if (nodesByPath.TryGetValue(row.NodePath, out var nodeId))
                staged.NodeId = nodeId;
            else if (Guid.TryParse(row.NodePath, out var byId) && nodeIds.Contains(byId))
                staged.NodeId = byId;
            else
                errors.Add($"no hierarchy node at '{row.NodePath}'");

            if (errors.Count > 0)
            {
                staged.Errors = [.. errors];
            }
            else if (!liveSites.TryGetValue(row.ExternalId, out var live))
            {
                staged.Action = row.Status == "closed" ? "unchanged" : "create";
            }
            else if (row.Status == "closed")
            {
                staged.Action = live.Status == "Closed" ? "unchanged" : "close";
            }
            else
            {
                var changes = new List<string>();
                if (live.Name != row.Name)
                    changes.Add($"name: {live.Name} -> {row.Name}");
                if (live.TimeZone != row.TimeZone)
                    changes.Add($"time_zone: {live.TimeZone} -> {row.TimeZone}");
                if (live.Status == "Closed")
                    changes.Add("status: Closed -> Open");
                staged.Action = changes.Count > 0 ? "update" : "unchanged";
                staged.Changes = [.. changes];
            }
            counts[staged.Action]++;
            db.StagedSites.Add(staged);
        }

        batch.Counts = System.Text.Json.JsonSerializer.Serialize(counts);
        db.Batches.Add(batch);
        await db.SaveChangesAsync(ct);
        return batch;
    }
}

/// <summary>Ingest's column mapping over the shared parser (Platform.Text.CsvParser).</summary>
public static class CsvParser
{
    public static List<Dictionary<string, string>> Parse(string text) =>
        LocIntel.Platform.Text.CsvParser.Parse(
            text,
            new(
                IngestLimits.MaxBytes,
                IngestLimits.MaxRows,
                IngestLimits.MaxColumns,
                IngestLimits.MaxFieldChars
            )
        );

    public static SourceRow ToSourceRow(Dictionary<string, string> record) =>
        new(
            record.GetValueOrDefault("external_id", ""),
            record.GetValueOrDefault("name", ""),
            record.GetValueOrDefault("time_zone", ""),
            record.GetValueOrDefault("node", ""),
            record.GetValueOrDefault("status", "open")
        );
}
