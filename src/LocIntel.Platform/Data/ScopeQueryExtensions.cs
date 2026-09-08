using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Data;

/// <summary>An entity that carries its hierarchy path (ADR 2/4): scope filters it by subtree.</summary>
public interface IPathScoped : IOrgScoped
{
    LTree Path { get; }
}

/// <summary>
/// A path-scoped entity that also stores its path as "C"-collated text
/// (ADR 51), so subtree scope is a leakproof btree range the app role can
/// use under row security. Large tables implement this; the ltree column
/// stays for semantics and for small tables.
/// </summary>
public interface IPathIndexed : IPathScoped
{
    string PathText { get; }
}

/// <summary>
/// The third gate applied to queries. The NodeScope argument is REQUIRED -
/// there is no overload without it, so "forgot to filter by location" is not
/// expressible. Subtree scope resolves as text-range predicates on the path
/// key where the entity has one (<see cref="IPathIndexed"/>), and as ltree
/// prefix predicates otherwise.
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
        var scoped = query.Where(e => e.OrgId == scope.Org);
        if (typeof(IPathIndexed).IsAssignableFrom(typeof(T)))
            return scoped.Where(PathKeys.UnderAny<T>(scope.Paths));
        var paths = scope.Paths.Select(p => new LTree(p)).ToArray();
        return scoped.Where(e => paths.Any(p => e.Path.IsDescendantOf(p)));
    }
}
