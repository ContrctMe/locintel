using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Data;

/// <summary>Entities addressed by a hierarchy path (sites, and anything site-attached).</summary>
public interface IPathScoped : IOrgScoped
{
    LTree Path { get; }
}

/// <summary>
/// The third gate applied to queries. The NodeScope argument is REQUIRED -
/// there is no overload without it, so "forgot to filter by location" is not
/// expressible. Subtree scope resolves as ltree prefix predicates
/// (path &lt;@ ANY(granted)), never id lists (ADR 3).
/// </summary>
public static class ScopeQueryExtensions
{
    public static IQueryable<T> InScope<T>(this IQueryable<T> query, NodeScope scope)
        where T : class, IPathScoped =>
        scope switch
        {
            NodeScope.EntireOrg org => query.Where(e => e.OrgId == org.Org),
            NodeScope.Subtrees s => ApplySubtrees(query, s),
            _ => query.Where(e => false), // None: empty, never an error
        };

    private static IQueryable<T> ApplySubtrees<T>(IQueryable<T> query, NodeScope.Subtrees scope)
        where T : class, IPathScoped
    {
        var paths = scope.Paths.Select(p => new LTree(p)).ToArray();
        return query.Where(e => e.OrgId == scope.Org && paths.Any(p => e.Path.IsDescendantOf(p)));
    }
}
