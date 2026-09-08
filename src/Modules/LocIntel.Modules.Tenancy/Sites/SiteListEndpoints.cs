using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Hierarchy;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Spatial;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// The fleet-scale reads over sites: the scoped, searched, paged list and the
/// open-now feed. Every predicate is a leakproof key range (ADR 51); the
/// write side lives in SiteEndpoints.
/// </summary>
public static class SiteListEndpoints
{
    /// <summary>
    /// Fleet-scale list: filtered by scope FIRST, then searched, then paged.
    /// Every predicate is a leakproof key range (ADR 51), so the app role's
    /// plan is an index range under row security however many sites the org
    /// has: scope and <c>under</c> are <c>path_text</c> ranges, the map's
    /// <c>bbox</c> (ADR 49) is a set of <c>cell</c> ranges made exact by the
    /// coordinates, search is "a word of the name or city starts with each
    /// word typed" over the term index, and a page is a keyset on (name, id):
    /// <c>after</c> is the <c>next</c> cursor the previous page returned.
    /// <c>zoom</c> stays in the contract for clustering (ADR 50 §4); today it
    /// is validated and unused.
    /// </summary>
    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/sites")]
    [ProducesResponseType(typeof(SiteListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Guid? under,
        string? q,
        string? status,
        string? bbox,
        int? zoom,
        int? limit,
        string? after,
        string? ids,
        CancellationToken ct
    )
    {
        SiteId[]? requestedIds = null;
        if (ids is not null)
        {
            var parts = ids.Split(',');
            if (parts.Length > 200 || parts.Any(part => !Guid.TryParse(part, out _)))
                return ApiErrors.BadRequest("ids must contain between 1 and 200 UUIDs");
            requestedIds = parts.Select(part => new SiteId(Guid.Parse(part))).Distinct().ToArray();
        }
        // a comma-separated set of lifecycle statuses (the console's filter bar)
        SiteStatus[]? statuses = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = new List<SiteStatus>();
            foreach (var part in status.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Enum.TryParse<SiteStatus>(part.Trim(), ignoreCase: true, out var one))
                    return ApiErrors.BadRequest($"unknown status '{part.Trim()}'");
                wanted.Add(one);
            }
            statuses = wanted.ToArray();
        }
        BoundingBox? box = null;
        if (bbox is not null)
        {
            if (!BoundingBox.TryParse(bbox, out var parsed, out var error))
                return Results.BadRequest(new { error });
            box = parsed;
        }
        if (zoom is < 0 or > 22)
            return ApiErrors.BadRequest("zoom must be between 0 and 22");
        SiteCursor? cursor = null;
        if (after is not null)
        {
            if (!SiteCursor.TryParse(after, out var parsedCursor))
                return ApiErrors.BadRequest("after is not a cursor this list issued");
            cursor = parsedCursor;
        }

        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        var query = db.Sites.InScope(scope);
        if (requestedIds is not null)
            query = query.Where(s => requestedIds.Contains(s.Id));
        if (under is { } nodeId)
        {
            var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == nodeId, ct);
            if (node is null)
                return Results.Ok(new SiteListResponse([], 0, 0, null));
            query = query.Where(PathKeys.UnderAny<Site>([node.Path.ToString()]));
        }
        if (statuses is not null)
            query = query.Where(s => statuses.Contains(s.Status));
        int? withoutCoordinates = null;
        if (box is { } viewport)
        {
            // Counted before the box so the map's list can name what it can
            // never show; a site without coordinates is inside no box.
            withoutCoordinates = await query.CountAsync(s => s.Cell == null, ct);
            query = query.Where(SpatialPredicates.InViewport<Site>(viewport));
        }
        var take = Math.Clamp(limit ?? 50, 1, 200);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var words = SiteSearchTerm.Words(q);
            var org = scope switch
            {
                NodeScope.EntireOrg entire => entire.Org,
                NodeScope.Subtrees subtrees => subtrees.Org,
                _ => (OrgId?)null,
            };
            if (words.Count == 0 || org is null)
                return Results.Ok(new SiteListResponse([], 0, 0, null, withoutCoordinates));
            return Results.Ok(
                await SearchAsync(db, query, org.Value, words, cursor, take, withoutCoordinates, ct)
            );
        }

        // one pass over the (org_id, status) index counts the whole result; a
        // viewport's count is capped like a search's - a continent-wide box at a
        // million sites is the whole org, and "10,000+ in view" is the answer
        var counted = box is null ? query : query.Take(SearchCountCap);
        var counts = await counted
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);
        var openCount = counts.Where(c => c.Status == SiteStatus.Open).Sum(c => c.Count);

        var page = query;
        if (cursor is { Name: var afterName, Id: var afterId })
            // "name >= n" is the index range; the rest skips the rows a
            // previous page already showed under the same name
            page = page.Where(s =>
                s.Name.CompareTo(afterName) >= 0
                && (s.Name.CompareTo(afterName) > 0 || s.Id.CompareTo(afterId) > 0)
            );
        var sites = await page.OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Take(take + 1)
            .ToListAsync(ct);
        var more = sites.Count > take;
        if (more)
            sites.RemoveAt(take);
        return Results.Ok(
            new SiteListResponse(
                sites.Select(SiteEndpoints.ToResponse).ToList(),
                total,
                openCount,
                more ? SiteCursor.Encode(sites[^1]) : null,
                withoutCoordinates,
                TotalIsLowerBound: box is not null && total >= SearchCountCap
            )
        );
    }

    /// <summary>
    /// Past this many matches a search or a viewport reports a lower bound: counting every
    /// hit of a common word is the one search cost that grows with the org.
    /// </summary>
    public const int SearchCountCap = 10_000;

    /// <summary>
    /// Search (ADR 51): the first word's term range IS the page - the
    /// <c>(org_id, term, name, site_id)</c> index walked in its own order,
    /// joined to the filtered sites, further words probed per row - so a page
    /// costs the same however the matches are spread through the org.
    /// Results therefore come back by matched term, then name; the cursor
    /// carries the term.
    /// </summary>
    private static async Task<SiteListResponse> SearchAsync(
        TenancyDbContext db,
        IQueryable<Site> sites,
        OrgId org,
        IReadOnlyList<string> words,
        SiteCursor? cursor,
        int take,
        int? withoutCoordinates,
        CancellationToken ct
    )
    {
        foreach (var word in words.Skip(1))
        {
            var (lo, hi) = SiteSearchTerm.PrefixRange(word);
            sites = sites.Where(s =>
                db.SiteSearchTerms.Any(t =>
                    t.SiteId == s.Id && t.Term.CompareTo(lo) >= 0 && t.Term.CompareTo(hi) < 0
                )
            );
        }
        var (first, last) = SiteSearchTerm.PrefixRange(words[0]);
        var hits = db
            .SiteSearchTerms.Where(t =>
                t.OrgId == org && t.Term.CompareTo(first) >= 0 && t.Term.CompareTo(last) < 0
            )
            .Join(sites, t => t.SiteId, s => s.Id, (t, s) => new { t, s });

        var counts = await hits.Take(SearchCountCap)
            .GroupBy(x => x.s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var total = counts.Sum(c => c.Count);
        var openCount = counts.Where(c => c.Status == SiteStatus.Open).Sum(c => c.Count);

        if (cursor is { Term: { } afterTerm, Name: var afterName, Id: var afterId })
            hits = hits.Where(x =>
                x.t.Term.CompareTo(afterTerm) >= 0
                && (
                    x.t.Term.CompareTo(afterTerm) > 0
                    || x.t.Name.CompareTo(afterName) > 0
                    || (x.t.Name == afterName && x.t.SiteId.CompareTo(afterId) > 0)
                )
            );
        var rows = await hits.OrderBy(x => x.t.Term)
            .ThenBy(x => x.t.Name)
            .ThenBy(x => x.t.SiteId)
            .Take(take + 1)
            .Select(x => new { x.s, x.t.Term })
            .ToListAsync(ct);
        var more = rows.Count > take;
        if (more)
            rows.RemoveAt(take);
        return new SiteListResponse(
            rows.Select(r => SiteEndpoints.ToResponse(r.s)).ToList(),
            total,
            openCount,
            more ? SiteCursor.Encode(rows[^1].s, rows[^1].Term) : null,
            withoutCoordinates,
            TotalIsLowerBound: total >= SearchCountCap
        );
    }

    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/sites/open-now")]
    public static async Task<IReadOnlyList<SiteResponse>> OpenNow(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.SitesRead, ct);
        var now = time.GetUtcNow();
        // The projection (ADR 28) makes this an indexed range query, not an
        // in-process RRULE expansion over every site.
        return
            await db
                .Sites.InScope(scope)
                .Where(s =>
                    db.SiteOpenWindows.Any(w =>
                        w.SiteId == s.Id && w.StartsAtUtc <= now && now < w.EndsAtUtc
                    )
                )
                .OrderBy(s => s.Name)
                .ToListAsync(ct)
                is { } open
            ? open.Select(SiteEndpoints.ToResponse).ToList()
            : [];
    }
}
