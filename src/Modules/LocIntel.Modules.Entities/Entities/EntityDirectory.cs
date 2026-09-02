using LocIntel.Contracts.Entities;
using LocIntel.Modules.Entities.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>The contract's promise: the fourth gate applies here too, for whoever is asking.</summary>
public sealed class EntityDirectory(
    EntitiesDbContext db,
    IPrincipalAccessor accessor,
    IScopeResolver scopes,
    TimeProvider time
) : IEntityDirectory
{
    public async Task<IReadOnlyDictionary<Guid, EntityInfo>> LookupVisibleAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken ct = default
    )
    {
        if (entityIds.Count == 0)
            return new Dictionary<Guid, EntityInfo>();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.EntitiesRead, ct);
        if (scope is NodeScope.None)
            return new Dictionary<Guid, EntityInfo>();
        var manage = await scopes.CanAsync(accessor.Current, Capabilities.EntitiesManage, ct);
        var ids = entityIds.ToArray();
        return await EntityVisibility
            .Visible(db, scope, accessor.Current, manage, time.GetUtcNow())
            .Where(e => ids.Contains(e.Id))
            .Select(e => new EntityInfo(
                e.Id,
                e.Kind.ToString(),
                e.Status.ToString(),
                e.DisplayName
            ))
            .ToDictionaryAsync(e => e.Id, ct);
    }
}
