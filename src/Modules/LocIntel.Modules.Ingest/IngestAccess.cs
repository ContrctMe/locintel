using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Ingest;

/// <summary>Imports and connectors administer the whole organization, including their staged history.</summary>
public static class IngestAccess
{
    public static async Task<bool> CanAsync(
        Principal principal,
        IScopeResolver scopes,
        CancellationToken ct
    ) =>
        await scopes.ScopeForAsync(principal, Capabilities.IngestManage, ct) is NodeScope.EntireOrg;

    public static async Task<GateOutcome> RequireUserAsync(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.IngestManage, ct);
        return gate is GateOutcome.Allowed { Scope: not NodeScope.EntireOrg }
            ? new GateOutcome.Forbidden(Capabilities.IngestManage)
            : gate;
    }
}
