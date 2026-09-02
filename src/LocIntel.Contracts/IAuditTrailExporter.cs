using LocIntel.Platform.Kernel;

namespace LocIntel.Contracts;

/// <summary>
/// The audit module's side of the tenant-facing trail export - the plugin
/// direction, like IOrgDataExporter. Storage assembles the archive; what a
/// "kind" is stays the audit module's business (ADR 12's four kinds).
/// </summary>
public interface IAuditTrailExporter
{
    Task<IReadOnlyList<AuditTrailSection>> ExportAsync(OrgId org, CancellationToken ct = default);
}
