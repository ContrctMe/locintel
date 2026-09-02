using LocIntel.Modules.Alerts.Bulletins;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Alerts.Alerts;

/// <summary>
/// Gate 3 for targeted rows with an OPTIONAL path: org-wide rows (null)
/// reach everyone; a targeted row reaches readers whose scope overlaps
/// it (their subtree lies under the target, or contains it).
/// </summary>
public static class ScopeOverlap
{
    public static IQueryable<Alert> Alerts(IQueryable<Alert> query, NodeScope scope)
    {
        switch (scope)
        {
            case NodeScope.EntireOrg:
                return query;
            case NodeScope.Subtrees s:
                var paths = s.Paths.Select(p => new LTree(p)).ToArray();
                return query.Where(a =>
                    a.Path == null
                    || paths.Any(p =>
                        a.Path!.Value.IsDescendantOf(p) || p.IsDescendantOf(a.Path!.Value)
                    )
                );
            default:
                return query.Where(a => false);
        }
    }

    public static IQueryable<Bulletin> Bulletins(IQueryable<Bulletin> query, NodeScope scope)
    {
        switch (scope)
        {
            case NodeScope.EntireOrg:
                return query;
            case NodeScope.Subtrees s:
                var paths = s.Paths.Select(p => new LTree(p)).ToArray();
                return query.Where(b =>
                    b.ScopePath == null
                    || paths.Any(p =>
                        b.ScopePath!.Value.IsDescendantOf(p) || p.IsDescendantOf(b.ScopePath!.Value)
                    )
                );
            default:
                return query.Where(b => false);
        }
    }
}
