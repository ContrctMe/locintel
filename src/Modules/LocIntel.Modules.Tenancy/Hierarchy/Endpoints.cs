using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Hierarchy;

public sealed record CreateHierarchyRequest(string Name, string[] Levels);

public sealed record CreateNodeRequest(Guid ParentId, string Name);

public sealed record MoveNodeRequest(Guid NewParentId);

public sealed record RenameNodeRequest(string Name);

public sealed record NodeResponse(Guid Id, Guid? ParentId, string Name, int Depth, string Path);

public static class HierarchyEndpoints
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/hierarchy")]
    public static async Task<IResult> Create(
        CreateHierarchyRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { ActiveOrg: { } org }
            || !await scopes.CanAsync(accessor.Current, Capabilities.HierarchyManage, ct)
        )
            return Results.Unauthorized();
        if (request.Levels.Length == 0)
            return Results.BadRequest(new { error = "at least one level is required" });

        // Gate 1: depth is entitlement-capped AT PROVISIONING (the register's
        // canonical structural-capability example).
        var depthLimit = await entitlements.LimitAsync(org, EntitlementCatalog.HierarchyDepth, ct);
        if (request.Levels.Length > depthLimit)
            return Results.Json(
                new
                {
                    error = "plan allows fewer hierarchy levels",
                    code = EntitlementCatalog.HierarchyDepth,
                    limit = depthLimit,
                    requested = request.Levels.Length,
                },
                statusCode: StatusCodes.Status402PaymentRequired
            );
        if (await db.Hierarchies.AnyAsync(h => h.IsAuthoritative, ct))
            return Results.Conflict(new { error = "hierarchy already exists" });

        var hierarchy = OrgHierarchy.Create(org, request.Name, request.Levels);
        var root = HierarchyNode.CreateRoot(org, hierarchy.Id, request.Name);
        db.Hierarchies.Add(hierarchy);
        db.HierarchyNodes.Add(root);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { hierarchy.Id, rootNodeId = root.Id });
    }

    [WolverineGet("/api/hierarchy")]
    public static async Task<IResult> Get(TenancyDbContext db, CancellationToken ct)
    {
        var hierarchy = await db.Hierarchies.FirstOrDefaultAsync(h => h.IsAuthoritative, ct);
        if (hierarchy is null)
            return Results.NotFound();
        var nodes = await db
            .HierarchyNodes.Where(n => n.HierarchyId == hierarchy.Id)
            .OrderBy(n => n.Path)
            .Select(n => new NodeResponse(n.Id, n.ParentId, n.Name, n.Depth, n.Path.ToString()))
            .ToListAsync(ct);
        return Results.Ok(
            new
            {
                hierarchy.Id,
                hierarchy.Name,
                hierarchy.Levels,
                nodes,
            }
        );
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/hierarchy/nodes")]
    public static async Task<IResult> CreateNode(
        CreateNodeRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var parent = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == request.ParentId, ct);
        if (parent is null)
            return Results.NotFound();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.HierarchyManage, ct);
        if (!scope.Covers(parent.Path.ToString()))
            return Results.Forbid();
        var hierarchy = await db.Hierarchies.FirstAsync(h => h.Id == parent.HierarchyId, ct);
        if (parent.Depth + 1 > hierarchy.Levels.Length)
            return Results.BadRequest(
                new
                {
                    error = $"hierarchy is limited to {hierarchy.Levels.Length} level(s) below the root",
                }
            );

        var node = HierarchyNode.CreateChild(parent, request.Name);
        db.HierarchyNodes.Add(node);
        await db.SaveChangesAsync(ct);
        return Results.Ok(
            new NodeResponse(node.Id, node.ParentId, node.Name, node.Depth, node.Path.ToString())
        );
    }

    /// <summary>
    /// Names are display-only: paths use id-derived labels (never the name),
    /// so a rename touches one row and no path, site, or stamped fact.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePut("/api/hierarchy/nodes/{id}")]
    public static async Task<IResult> RenameNode(
        Guid id,
        RenameNodeRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Wolverine.IMessageBus bus,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            return Results.BadRequest(new { error = "name must be 1-200 characters" });
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (node is null)
            return Results.NotFound();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.HierarchyManage, ct);
        if (!scope.Covers(node.Path.ToString()))
            return Results.Forbid();

        var previous = node.Name;
        node.Name = request.Name.Trim();
        await db.SaveChangesAsync(ct);
        await PublishNodeAudit(
            bus,
            accessor,
            node,
            "hierarchy.node_renamed",
            new
            {
                nodeId = node.Id,
                from = previous,
                to = node.Name,
            }
        );
        return Results.NoContent();
    }

    /// <summary>
    /// Delete an EMPTY leaf: no child nodes, no sites hanging from it. Sites
    /// are never orphaned (they close, never delete - ADR 25) and subtrees
    /// are dismantled deliberately, bottom-up. A role scoped to the deleted
    /// path simply covers nothing from now on - the module ladder keeps
    /// Tenancy from reading Identity's assignments, and grant evaluation is
    /// monotonic, so a dangling scope is inert, not dangerous.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverineDelete("/api/hierarchy/nodes/{id}")]
    public static async Task<IResult> DeleteNode(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Wolverine.IMessageBus bus,
        CancellationToken ct
    )
    {
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (node is null)
            return Results.NotFound();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.HierarchyManage, ct);
        if (!scope.Covers(node.Path.ToString()))
            return Results.Forbid();
        if (node.ParentId is null)
            return Results.BadRequest(new { error = "the root cannot be deleted" });

        var children = await db.HierarchyNodes.CountAsync(n => n.ParentId == id, ct);
        if (children > 0)
            return Results.Conflict(
                new { error = "node has child nodes - move or delete them first", children }
            );
        var sites = await db.Sites.CountAsync(s => s.NodeId == id, ct);
        if (sites > 0)
            return Results.Conflict(
                new { error = "sites are attached to this node - move them first", sites }
            );

        db.HierarchyNodes.Remove(node);
        await db.SaveChangesAsync(ct);
        await PublishNodeAudit(
            bus,
            accessor,
            node,
            "hierarchy.node_deleted",
            new
            {
                nodeId = node.Id,
                node.Name,
                path = node.Path.ToString(),
            }
        );
        return Results.NoContent();
    }

    private static async Task PublishNodeAudit(
        Wolverine.IMessageBus bus,
        IPrincipalAccessor accessor,
        HierarchyNode node,
        string eventName,
        object payload
    )
    {
        var actor = accessor.Current as Principal.User;
        await bus.PublishAsync(
            new LocIntel.Contracts.RecordDomainAudit(
                eventName,
                System.Text.Json.JsonSerializer.Serialize(payload)
            ),
            new Wolverine.DeliveryOptions
            {
                TenantId = node.OrgId.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = actor?.UserId.ToString() ?? "",
                },
            }
        );
    }

    /// <summary>
    /// Re-parenting (ADR 2): rewrites the subtree's materialized paths - nodes
    /// and the denormalized site paths - in two set-based statements. History
    /// stays honest because FACT tables stamp paths at write time; projections
    /// join live rows and need no rewrite.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/hierarchy/nodes/{id}/move")]
    public static async Task<IResult> MoveNode(
        Guid id,
        MoveNodeRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        Wolverine.IMessageBus bus,
        CancellationToken ct
    )
    {
        var moveScope = await scopes.ScopeForAsync(
            accessor.Current,
            Capabilities.HierarchyManage,
            ct
        );
        var node = await db.HierarchyNodes.FirstOrDefaultAsync(n => n.Id == id, ct);
        var newParent = await db.HierarchyNodes.FirstOrDefaultAsync(
            n => n.Id == request.NewParentId,
            ct
        );
        if (node is null || newParent is null)
            return Results.NotFound();
        if (!moveScope.Covers(node.Path.ToString()) || !moveScope.Covers(newParent.Path.ToString()))
            return Results.Forbid();
        if (node.ParentId is null)
            return Results.BadRequest(new { error = "the root cannot move" });
        // in-memory prefix check (LTree.IsDescendantOf only translates in queries)
        var nodePrefix = node.Path.ToString();
        var parentPath = newParent.Path.ToString();
        if (parentPath == nodePrefix || parentPath.StartsWith(nodePrefix + '.'))
            return Results.BadRequest(new { error = "cannot move a node under its own subtree" });

        var oldPrefix = node.Path.ToString();
        var newPrefix = $"{newParent.Path}.{HierarchyNode.Label(node.Id)}";
        var depthDelta = newParent.Depth + 1 - node.Depth;

        var hierarchy = await db.Hierarchies.FirstAsync(h => h.Id == node.HierarchyId, ct);
        var subtreeDepth = await db
            .HierarchyNodes.Where(n => n.Path.IsDescendantOf(node.Path))
            .MaxAsync(n => n.Depth, ct);
        if (subtreeDepth + depthDelta > hierarchy.Levels.Length)
            return Results.BadRequest(new { error = "move would exceed the hierarchy's depth" });

        node.ParentId = newParent.Id;
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE tenancy.hierarchy_nodes
            SET path = CASE WHEN path = {oldPrefix}::ltree THEN {newPrefix}::ltree
                       ELSE ({newPrefix}::ltree || subpath(path, nlevel({oldPrefix}::ltree))) END,
                depth = depth + {depthDelta}
            WHERE path <@ {oldPrefix}::ltree
            """,
            ct
        );
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE tenancy.sites
            SET path = ({newPrefix}::ltree || subpath(path, nlevel({oldPrefix}::ltree)))
            WHERE path <@ {oldPrefix}::ltree
            """,
            ct
        );
        // Intent-level audit (ADR 12): a reorg is a business event, not just
        // row diffs - record WHAT happened in business language.
        var actor = accessor.Current as Principal.User;
        await bus.PublishAsync(
            new LocIntel.Contracts.RecordDomainAudit(
                "hierarchy.node_moved",
                System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        nodeId = node.Id,
                        nodeName = node.Name,
                        from = oldPrefix,
                        to = newPrefix,
                    }
                )
            ),
            new Wolverine.DeliveryOptions
            {
                TenantId = node.OrgId.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = actor?.UserId.ToString() ?? "",
                },
            }
        );
        return Results.NoContent();
    }
}
