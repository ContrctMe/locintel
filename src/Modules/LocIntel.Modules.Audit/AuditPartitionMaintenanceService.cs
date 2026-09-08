using LocIntel.Platform.Messaging;

namespace LocIntel.Modules.Audit;

public sealed class AuditPartitionMaintenanceService(IServiceProvider services)
    : GlobalSweepService<MaintainAuditPartitions>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
}
