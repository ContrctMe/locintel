using LocIntel.Platform.Messaging;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The SLA enumerator (ADR 24): every five minutes, one tenant-scoped sweep
/// per org, on the Platform sweep base. Worker role only.
/// </summary>
public sealed class RequestSlaService(IServiceProvider services)
    : PerOrgSweepService<EscalateOverdueRequests>(services)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);
}
