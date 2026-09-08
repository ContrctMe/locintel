using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Entitlements.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Entitlements;

public sealed record OperatorSetEntitlementRequest(string Value);

public sealed record OperatorAddExceptionRequest(
    string Value,
    string Reason,
    DateTimeOffset ExpiresAt
);

public sealed record EntitlementExceptionCreatedResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>
/// Entitlement CUSTODY (the operator persona): plan values and exceptions
/// are operator-set, tenant-read. Every endpoint targets an org id and runs
/// its data work inside that org's tenant scope - RLS applies to the target,
/// and the audit trail lands in the target org attributed to the operator.
/// </summary>
public static class OperatorEntitlementEndpoints
{
    // data work happens inside TenantScope's inner scope (its own context,
    // its own transaction) - the outer chain must not open one
    [NonTransactional]
    [WolverineGet("/api/operator/orgs/{orgId}/entitlements")]
    [ProducesResponseType(typeof(Dictionary<string, EntitlementSummary>), StatusCodes.Status200OK)]
    public static async Task<IResult> Effective(
        Guid orgId,
        HttpContext http,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var target = new OrgId(orgId);
        return await TenantScope.RunAsAsync(
            http.RequestServices,
            target,
            async sp =>
            {
                var service = sp.GetRequiredService<EntitlementsService>();
                var probes = sp.GetServices<IEntitlementUsageProbe>();
                return Results.Ok(await service.DescribeAllAsync(target, probes, ct));
            }
        );
    }

    /// <summary>Downgrade preflight (ADR 11) runs against the TARGET org's usage.</summary>
    // data work happens inside TenantScope's inner scope (its own context,
    // its own transaction) - the outer chain must not open one
    [NonTransactional]
    [WolverinePut("/api/operator/orgs/{orgId}/entitlements/{code}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Set(
        Guid orgId,
        string code,
        OperatorSetEntitlementRequest request,
        HttpContext http,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
        if (
            gate is not GateOutcome.Allowed { Principal: Principal.User { UserId: var operatorId } }
        )
            return gate.ToResult();
        if (!EntitlementCatalog.Definitions.TryGetValue(code, out var descriptor))
            return Results.NotFound();

        var target = new OrgId(orgId);
        return await TenantScope.RunAsAsync(
            http.RequestServices,
            target,
            async sp =>
            {
                if (descriptor.Shape is EntitlementShape.Limit)
                {
                    var probes = sp.GetServices<IEntitlementUsageProbe>();
                    if (probes.FirstOrDefault(p => p.Code == code) is { } probe)
                    {
                        var newLimit = long.Parse(request.Value);
                        var current = await probe.CurrentUsageAsync(target, ct);
                        if (current > newLimit)
                            return ApiErrors.Status(
                                "downgrade blocked until conformant",
                                StatusCodes.Status409Conflict,
                                code,
                                new
                                {
                                    currentUsage = current,
                                    requestedLimit = newLimit,
                                    over = current - newLimit,
                                }
                            );
                    }
                }

                var db = sp.GetRequiredService<EntitlementsDbContext>();
                var row = await db.OrgEntitlements.FirstOrDefaultAsync(
                    e => e.OrgId == target && e.Code == code,
                    ct
                );
                if (row is null)
                {
                    db.OrgEntitlements.Add(
                        new OrgEntitlement
                        {
                            Id = Guid.CreateVersion7(),
                            OrgId = target,
                            Code = code,
                            Value = request.Value,
                            Source = "operator",
                        }
                    );
                }
                else
                {
                    row.Value = request.Value;
                    row.Source = "operator";
                    row.UpdatedAt = DateTimeOffset.UtcNow;
                }
                await db.SaveChangesAsync(ct);

                await sp.GetRequiredService<Wolverine.IMessageBus>()
                    .AuditAsync(
                        target,
                        AuditActor.User(operatorId),
                        "entitlement.set",
                        new { code, request.Value }
                    );
                return Results.NoContent();
            }
        );
    }

    // data work happens inside TenantScope's inner scope (its own context,
    // its own transaction) - the outer chain must not open one
    [NonTransactional]
    [WolverinePost("/api/operator/orgs/{orgId}/entitlements/{code}/exceptions")]
    [ProducesResponseType(typeof(EntitlementExceptionCreatedResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> AddException(
        Guid orgId,
        string code,
        OperatorAddExceptionRequest request,
        HttpContext http,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
        if (
            gate is not GateOutcome.Allowed { Principal: Principal.User { UserId: var operatorId } }
        )
            return gate.ToResult();
        if (!EntitlementCatalog.Definitions.ContainsKey(code))
            return Results.NotFound();
        if (request.ExpiresAt <= DateTimeOffset.UtcNow)
            return ApiErrors.BadRequest("exceptions must expire in the future");

        var target = new OrgId(orgId);
        return await TenantScope.RunAsAsync(
            http.RequestServices,
            target,
            async sp =>
            {
                var db = sp.GetRequiredService<EntitlementsDbContext>();
                db.Exceptions.Add(
                    new EntitlementException
                    {
                        Id = Guid.CreateVersion7(),
                        OrgId = target,
                        Code = code,
                        Value = request.Value,
                        Reason = request.Reason,
                        GrantedBy = operatorId,
                        ExpiresAt = request.ExpiresAt,
                    }
                );
                await db.SaveChangesAsync(ct);
                return Results.Ok(new EntitlementExceptionCreatedResponse(code, request.ExpiresAt));
            }
        );
    }
}
