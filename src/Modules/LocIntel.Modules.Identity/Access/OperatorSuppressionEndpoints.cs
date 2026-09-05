using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Access;

public sealed record SuppressionResponse(
    Guid Id,
    string Email,
    string Reason,
    DateTimeOffset CreatedAt
);

/// <summary>
/// The knob the product promises (maturity review, hole 3): contact-link
/// issuance tells users to "ask the operator to unsuppress" a bounced
/// address - this is that surface. Suppressions are platform-global (an
/// undeliverable address is undeliverable for every org), so custody is the
/// operator's; unsuppression is row deletion, after which sending resumes.
/// </summary>
public static class OperatorSuppressionEndpoints
{
    [Transactional(typeof(IdentityDbContext))]
    [WolverineGet("/api/operator/suppressions")]
    [ProducesResponseType(typeof(List<SuppressionResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        string? q,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var query = db.EmailSuppressions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(s => EF.Functions.ILike(s.Email, $"%{q.Trim()}%"));
        var rows = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .Select(s => new
            {
                s.Id,
                s.Email,
                s.Reason,
                s.CreatedAt,
            })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    [Transactional(typeof(IdentityDbContext))]
    [WolverineDelete("/api/operator/suppressions/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Unsuppress(
        Guid id,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireOperatorAsync(accessor, operators, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var suppression = await db.EmailSuppressions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (suppression is null)
            return Results.NotFound();
        db.EmailSuppressions.Remove(suppression);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
