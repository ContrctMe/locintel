using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Attributes;
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
    [Transactional(typeof(AuditDbContext))]
    [WolverinePost("/api/audit/export")]
    [ProducesResponseType(typeof(AuditExportQueuedResponse), StatusCodes.Status202Accepted)]
    public static async Task<IResult> Export(
        AuditDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.AuditRead, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        if (((GateOutcome.Allowed)gate).Scope is not NodeScope.EntireOrg)
            return Results.Forbid();
        var userId = principal.UserId;
        if (await ExportAdmission.TryReserveAsync(db, org, ct) is not { } admission)
            return ApiErrors.Status(
                "an export is already queued or running",
                StatusCodes.Status429TooManyRequests
            );
        await bus.PublishAsync(
            new ExportAuditTrail(userId, admission),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );
        return Results.Accepted(value: new AuditExportQueuedResponse("queued", "files"));
    }
}
