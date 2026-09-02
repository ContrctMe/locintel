using LocIntel.Modules.Entities.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// The fourth gate (blueprint: need-to-know). After entitlement, grant, and
/// scope, an entity is visible to a principal only when:
///  - they hold entities:manage (investigators see the org's whole graph), or
///  - it links to an incident whose stamped path lies in their entities:read
///    scope (the link table carries the path, so this is one ltree predicate), or
///  - a manager granted them this entity and the grant is unexpired.
/// Like gate 3 it never fails - it filters; an invisible id is a 404.
/// </summary>
public static class EntityVisibility
{
    public static IQueryable<Entity> Visible(
        EntitiesDbContext db,
        NodeScope scope,
        Principal principal,
        bool manage,
        DateTimeOffset now
    )
    {
        if (manage)
            return db.Entities;
        var links = db.Links.InScope(scope);
        var userId = principal is Principal.User user ? user.UserId : Guid.Empty;
        return db.Entities.Where(e =>
            links.Any(l => l.EntityId == e.Id)
            || db.Grants.Any(g => g.EntityId == e.Id && g.UserId == userId && g.ExpiresAt > now)
        );
    }
}
