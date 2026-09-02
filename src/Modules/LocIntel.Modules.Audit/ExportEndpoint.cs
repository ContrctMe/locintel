using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Wolverine;
using Wolverine.Http;

namespace LocIntel.Modules.Audit;

/// <summary>
/// Tenant-facing compliance export: the full trail (all four kinds, the whole
/// retained window) as a JSONL archive delivered to Files - same custody and
/// download path as everything else the org owns.
/// </summary>
public static class ExportEndpoint
{
    [WolverinePost("/api/audit/export")]
    public static async Task<IResult> Export(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } org, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.AuditRead, ct)
        )
            return Results.Unauthorized();
        await bus.PublishAsync(
            new ExportAuditTrail(userId),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return Results.Accepted(value: new { status = "queued", destination = "files" });
    }
}
