using LocIntel.Platform.Kernel;

namespace LocIntel.Contracts;

/// <summary>Storage-owned clean files for reports, with explicit organization selection.</summary>
public interface IReportFileSource
{
    Task<IReadOnlyList<File>> ReadAsync(OrgId org, Guid[] ids, CancellationToken ct = default);

    public sealed record File(Guid Id, string Key, string Name, string ContentType, Guid[] SiteIds);
}
