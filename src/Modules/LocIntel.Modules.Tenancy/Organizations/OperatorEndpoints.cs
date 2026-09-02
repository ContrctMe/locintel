using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

/// <summary>Org lifecycle, operator custody: the back half the entities always modeled.</summary>
public static class OperatorOrgEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/operator/orgs")]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct
    )
    {
        if (!await operators.IsOperatorAsync(accessor.Current, ct))
            return Results.Unauthorized();
        var orgs = await db
            .Organizations.OrderBy(o => o.Name)
            .Select(o => new
            {
                id = o.Id.Value,
                o.Name,
                o.Slug,
                status = o.Status.ToString(),
                o.IsPlatform,
                o.CreatedAt,
            })
            .ToListAsync(ct);
        return Results.Ok(orgs);
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/operator/orgs/{orgId}/suspend")]
    public static Task<IResult> Suspend(
        Guid orgId,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        Transition(
            orgId,
            OrganizationStatus.Suspended,
            "org.suspended",
            db,
            accessor,
            operators,
            bus,
            ct
        );

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/operator/orgs/{orgId}/reactivate")]
    public static Task<IResult> Reactivate(
        Guid orgId,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        IMessageBus bus,
        CancellationToken ct
    ) =>
        Transition(
            orgId,
            OrganizationStatus.Active,
            "org.reactivated",
            db,
            accessor,
            operators,
            bus,
            ct
        );

    private static async Task<IResult> Transition(
        Guid orgId,
        OrganizationStatus status,
        string eventName,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { UserId: var operatorId } principal
            || !await operators.IsOperatorAsync(principal, ct)
        )
            return Results.Unauthorized();
        var target = new OrgId(orgId);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == target, ct);
        if (org is null)
            return Results.NotFound();
        if (org.IsPlatform)
            return Results.BadRequest(new { error = "the platform org cannot be suspended" });
        if (org.Status == status)
            return Results.NoContent();

        org.Status = status;
        await db.SaveChangesAsync(ct);

        // read models (and therefore ENFORCEMENT) learn via the same event
        await bus.PublishAsync(
            new OrganizationUpserted(
                org.Id,
                org.Name,
                org.Slug,
                org.Region,
                org.ExternalId,
                org.Status.ToString(),
                org.IsPlatform
            )
        );
        await bus.AuditAsync(org.Id, AuditActor.User(operatorId), eventName, new { });
        return Results.NoContent();
    }
}
