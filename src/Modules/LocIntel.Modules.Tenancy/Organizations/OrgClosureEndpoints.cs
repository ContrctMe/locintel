using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

public sealed record ClosureStatusResponse(DateTimeOffset? RequestedAt, DateTimeOffset? PurgesAt);

/// <summary>
/// Self-serve org closure (operability item 7): a manager requests it, the
/// org stays ACTIVE and cancelable through a grace window (default 30 days,
/// Organizations:CloseGraceDays), every manager is notified with the date,
/// and the daily sweep offboards it when the window closes. Deliberate by
/// construction: nothing purges the moment a button is clicked.
/// </summary>
public static class OrgClosureEndpoints
{
    public static int GraceDays(IConfiguration configuration) =>
        configuration.GetValue<int?>("Organizations:CloseGraceDays") ?? 30;

    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/org/closure")]
    [ProducesResponseType(typeof(ClosureStatusResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Status(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IConfiguration configuration,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } orgId } principal
            || !await scopes.CanAsync(principal, Capabilities.OrgManage, ct)
        )
            return Results.Unauthorized();
        var requestedAt = await db
            .Organizations.Where(o => o.Id == orgId)
            .Select(o => o.CloseRequestedAt)
            .FirstOrDefaultAsync(ct);
        return Results.Ok(
            new ClosureStatusResponse(requestedAt, requestedAt?.AddDays(GraceDays(configuration)))
        );
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/org/close")]
    [ProducesResponseType(typeof(ClosureStatusResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Request(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IConfiguration configuration,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } orgId, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.OrgManage, ct)
        )
            return Results.Unauthorized();
        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
        if (org.IsPlatform)
            return Results.BadRequest(new { error = "the platform org cannot be closed" });
        if (org.CloseRequestedAt is not null)
            return Results.NoContent(); // already pending: idempotent

        org.CloseRequestedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var purgesAt = org.CloseRequestedAt.Value.AddDays(GraceDays(configuration));

        await bus.PublishAsync(
            new RecordDomainAudit(
                "org.close_requested",
                System.Text.Json.JsonSerializer.Serialize(new { purgesAt })
            ),
            new DeliveryOptions
            {
                TenantId = orgId.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = userId.ToString(),
                },
            }
        );
        await bus.PublishAsync(
            new SendOrgNotice(
                "Your organization is scheduled to close",
                [
                    $"A manager requested closure of {org.Name}. All data will be permanently deleted on {purgesAt:MMMM d, yyyy}.",
                    "Until then everything keeps working, and any manager can cancel from Settings.",
                    "Export your data first: Settings -> Your data -> Export org data.",
                ]
            ),
            new DeliveryOptions { TenantId = orgId.Value.ToString() }
        );
        return Results.Ok(new ClosureStatusResponse(org.CloseRequestedAt, purgesAt));
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/org/close/cancel")]
    public static async Task<IResult> Cancel(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } orgId, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.OrgManage, ct)
        )
            return Results.Unauthorized();
        var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
        if (org.CloseRequestedAt is null)
            return Results.NoContent();

        org.CloseRequestedAt = null;
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new RecordDomainAudit("org.close_canceled", "{}"),
            new DeliveryOptions
            {
                TenantId = orgId.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = userId.ToString(),
                },
            }
        );
        return Results.NoContent();
    }
}
