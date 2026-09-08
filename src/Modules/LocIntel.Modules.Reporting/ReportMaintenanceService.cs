using LocIntel.Platform.Messaging;

namespace LocIntel.Modules.Reporting;

public sealed class ReportMaintenanceService(IServiceProvider services)
    : PerOrgSweepService<MaintainReports>(services)
{
    /// <summary>
    /// How often the sweep runs, and therefore how stale queued work must be
    /// before the sweep re-delivers it: anything younger was just handed to
    /// Wolverine, which is the primary path. The sweep is recovery.
    /// </summary>
    public static readonly TimeSpan Sweep = TimeSpan.FromMinutes(1);

    protected override TimeSpan Interval => Sweep;
}
