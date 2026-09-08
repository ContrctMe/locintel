using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

public sealed record OperatedOrgResponse(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    bool IsPlatform,
    DateTimeOffset CreatedAt
);

/// <summary>Org lifecycle, operator custody: the back half the entities always modeled.</summary>
public static class OperatorOrgEndpoints
{
    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/operator/orgs")]
    [ProducesResponseType(typeof(List<OperatedOrgResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
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
        var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
        if (
            gate is not GateOutcome.Allowed { Principal: Principal.User { UserId: var operatorId } }
        )
            return gate.ToResult();
        var target = new OrgId(orgId);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == target, ct);
        if (org is null)
            return Results.NotFound();
        if (org.IsPlatform)
            return ApiErrors.BadRequest("the platform org cannot be suspended");
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
                org.Version,
                org.Status.ToString(),
                org.IsPlatform
            )
        );
        await bus.AuditAsync(org.Id, AuditActor.User(operatorId), eventName, new { });
        return Results.NoContent();
    }
}
