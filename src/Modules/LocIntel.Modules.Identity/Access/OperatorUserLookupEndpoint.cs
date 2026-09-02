using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Identity.Access;

/// <summary>
/// "A ticket from jane@customer.com - which org is she?" (maturity review,
/// hole 2). Case-insensitive search over email and name, each hit carrying
/// the person's orgs, so support starts from the ticket's From line instead
/// of a database query. Platform-global reads (users, memberships,
/// org_directory) - operator custody, like the rest of the wall.
/// </summary>
public static class OperatorUserLookupEndpoint
{
    [Transactional(typeof(IdentityDbContext))]
    [WolverineGet("/api/operator/users")]
    public static async Task<IResult> Search(
        string q,
        IdentityDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        CancellationToken ct
    )
    {
        if (!await operators.IsOperatorAsync(accessor.Current, ct))
            return Results.Unauthorized();
        var term = q.Trim();
        if (term.Length < 2)
            return Results.BadRequest(new { error = "search needs at least 2 characters" });

        var users = await db
            .Users.Where(u =>
                EF.Functions.ILike(u.Email, $"%{term}%")
                || (u.Name != null && EF.Functions.ILike(u.Name, $"%{term}%"))
            )
            .OrderBy(u => u.Email)
            .Take(20)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.Name,
                u.CreatedAt,
                orgs = db
                    .Memberships.Where(m => m.UserId == u.Id)
                    .Join(
                        db.OrgDirectory,
                        m => m.OrgId,
                        d => d.OrgId,
                        (m, d) =>
                            new
                            {
                                id = d.OrgId.Value,
                                d.Name,
                                d.Status,
                            }
                    )
                    .OrderBy(o => o.Name)
                    .ToList(),
            })
            .ToListAsync(ct);
        return Results.Ok(users);
    }
}
