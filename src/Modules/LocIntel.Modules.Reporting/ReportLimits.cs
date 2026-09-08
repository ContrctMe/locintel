using Microsoft.Extensions.Configuration;

namespace LocIntel.Modules.Reporting;

/// <summary>Operational resource budgets; independent of billable entitlements.</summary>
public sealed class ReportLimits(IConfiguration configuration)
{
    public int MaxBatchItems { get; } = Read(configuration, "MaxBatchItems", 100, 1000);
    public int MaxAggregateSites { get; } = Read(configuration, "MaxAggregateSites", 100, 1000);
    public int MaxQueuedJobsPerOrg { get; } = Read(configuration, "MaxQueuedJobsPerOrg", 10, 100);
    public int ItemTimeoutSeconds { get; } = Read(configuration, "ItemTimeoutSeconds", 60, 300);
    public int ArtifactDays { get; } = Read(configuration, "ArtifactDays", 7, 365);
    public int MetadataDays { get; } = Read(configuration, "MetadataDays", 30, 730);
    public long MaxPdfBytes { get; } = Read(configuration, "MaxPdfMiB", 20, 100) * 1024L * 1024;
    public long MaxZipBytes { get; } = Read(configuration, "MaxZipMiB", 200, 1024) * 1024L * 1024;

    private static int Read(IConfiguration configuration, string key, int fallback, int ceiling)
    {
        var value = configuration.GetValue("Reports:" + key, fallback);
        return value is > 0 && value <= ceiling
            ? value
            : throw new InvalidOperationException($"Reports:{key} must be 1..{ceiling}.");
    }
}
