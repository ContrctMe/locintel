using LocIntel.Platform.Messaging;

namespace LocIntel.Modules.Entities.Retention;

/// <summary>
/// The daily retention enumerator (ADR 24): one tenant-scoped sweep per org,
/// on the Platform sweep base. Worker role only.
/// </summary>
public sealed class EntityRetentionService(IServiceProvider services)
    : PerOrgSweepService<ExpireEntities>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(24);
}
