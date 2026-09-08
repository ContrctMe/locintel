using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting.Data;

/// <summary>
/// Tier 3 accounting record, independent of artifact/job retention. Id is the PDF
/// item id. PeriodMonth is the UTC calendar month of admission, not a site date.
/// Reserved entries survive crashes; consumed entries survive metadata deletion.
/// </summary>
public sealed class ReportQuotaEntry : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid JobId { get; init; }
    public required DateOnly PeriodMonth { get; init; }
    public bool Consumed { get; set; }
}
