using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace LocIntel.Modules.Audit;

public sealed record AuditExportQueuedResponse(string Status, string Destination);

/// <summary>
/// Tenant-facing compliance export: the full trail (all four kinds, the whole
/// retained window) as a JSONL archive delivered to Files - same custody and
/// download path as everything else the org owns.
/// </summary>
public static class ExportEndpoint
{
    [WolverinePost("/api/audit/export")]
    [ProducesResponseType(typeof(AuditExportQueuedResponse), StatusCodes.Status202Accepted)]
    public static async Task<IResult> Export(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.AuditRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        await bus.PublishAsync(
            new ExportAuditTrail(userId),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return Results.Accepted(value: new AuditExportQueuedResponse("queued", "files"));
    }
}
