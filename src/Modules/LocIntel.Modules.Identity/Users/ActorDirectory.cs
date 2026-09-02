using Microsoft.EntityFrameworkCore;
using LocIntel.Contracts;
using LocIntel.Modules.Identity.Data;

namespace LocIntel.Modules.Identity.Users;

public sealed class ActorDirectory(IdentityDbContext db) : IActorDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> LabelsAsync(
        IReadOnlyCollection<Guid> actorIds,
        CancellationToken ct = default
    ) =>
        await db
            .Users.Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);
}
