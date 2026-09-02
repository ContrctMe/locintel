using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Audit;

/// <summary>
/// The full compliance export (distinct from the offboarding SUMMARY in
/// AuditExporter): all four kinds (ADR 12) as JSONL, one file per kind.
/// The retention purge already bounds the tables, so "everything present"
/// IS the entitled window; the per-kind cap only guards archive assembly,
/// and a hit is marked so nobody mistakes a truncated export for complete.
/// </summary>
public sealed class AuditTrailExporter(AuditDbContext db) : IAuditTrailExporter
{
    private const int MaxRowsPerKind = 100_000;

    public async Task<IReadOnlyList<AuditTrailSection>> ExportAsync(
        OrgId org,
        CancellationToken ct = default
    )
    {
        return
        [
            await SectionAsync(
                "changes",
                db.Changes.Where(a => a.OrgId == org.Value)
                    .OrderByDescending(a => a.OccurredAt)
                    .Select(a => new
                    {
                        a.OccurredAt,
                        a.ActorTier,
                        a.ActorLabel,
                        a.SchemaName,
                        a.TableName,
                        a.RowId,
                        a.Operation,
                        diff = a.Diff,
                    }),
                ct
            ),
            await SectionAsync(
                "events",
                db.DomainEvents.Where(a => a.OrgId == org.Value)
                    .OrderByDescending(a => a.OccurredAt)
                    .Select(a => new
                    {
                        a.OccurredAt,
                        a.ActorTier,
                        a.ActorId,
                        a.EventName,
                        payload = a.Payload,
                    }),
                ct
            ),
            await SectionAsync(
                "authz",
                db.AuthzDecisions.Where(a => a.OrgId == org.Value)
                    .OrderByDescending(a => a.OccurredAt)
                    .Select(a => new
                    {
                        a.OccurredAt,
                        a.ActorTier,
                        a.ActorId,
                        a.Action,
                        a.Outcome,
                        a.ScopeSummary,
                    }),
                ct
            ),
            await SectionAsync(
                "access",
                db.Accesses.Where(a => a.OrgId == org.Value)
                    .OrderByDescending(a => a.OccurredAt)
                    .Select(a => new
                    {
                        a.OccurredAt,
                        a.ActorTier,
                        a.ActorId,
                        a.Method,
                        a.Path,
                        a.StatusCode,
                    }),
                ct
            ),
        ];
    }

    private static async Task<AuditTrailSection> SectionAsync<T>(
        string kind,
        IQueryable<T> rows,
        CancellationToken ct
    )
    {
        var page = await rows.Take(MaxRowsPerKind + 1).ToListAsync(ct);
        var truncated = page.Count > MaxRowsPerKind;
        var builder = new StringBuilder();
        foreach (var row in page.Take(MaxRowsPerKind))
            builder.AppendLine(JsonSerializer.Serialize(row, JsonSerializerOptions.Web));
        return new AuditTrailSection(kind, builder.ToString(), truncated);
    }
}
