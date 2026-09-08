using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Spatial.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Spatial.Overlays;

public sealed record CreateOverlayLayerRequest(
    string Name,
    string Kind,
    JsonElement? Style = null,
    Guid? NodeId = null
);

public sealed record UpdateOverlayLayerRequest(string? Name = null, JsonElement? Style = null);

/// <summary>A GeoJSON FeatureCollection (or one Feature, or a bare polygon) that becomes the layer's whole feature set.</summary>
public sealed record OverlayFeaturesUpload(JsonElement GeoJson);

public sealed record OverlayLayerSummary(
    Guid Id,
    string Name,
    string Kind,
    JsonElement? Style,
    Guid NodeId,
    int FeatureCount,
    int Version,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt
);

public sealed record OverlayLayerListResponse(IReadOnlyList<OverlayLayerSummary> Layers);

public sealed record OverlayLayerState(Guid Id, int Version, DateTimeOffset? DeletedAt);

public sealed record OverlayFeaturesReplaced(Guid LayerId, int Count, int Version);

/// <summary>
/// Overlay editing (ADR 50 §3): the org's own shapes, kept in the org's own
/// rows. Readers hold overlays:read and see the layers whose anchor their
/// scope covers; editors hold overlays:manage and may only anchor a layer to
/// a node their scope covers. The first version accepts GeoJSON uploads of
/// polygons; drawing in place is the console's job over the same endpoint.
/// </summary>
public static class OverlayEndpoints
{
    [Transactional(
        typeof(SpatialDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/overlays")]
    [ProducesResponseType(typeof(OverlayLayerListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        bool? deleted,
        SpatialDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.OverlaysRead, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();

        // the trash is the same list with the soft-delete filter off; the
        // tenant filter stays on (ModuleDbContext: never disable both)
        var query =
            deleted == true
                ? db
                    .Layers.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
                    .Where(l => l.DeletedAt != null)
                : db.Layers.AsQueryable();
        var layers = await query
            .InScope(scope)
            .OrderBy(l => l.Name)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.Kind,
                l.Style,
                l.NodeId,
                l.Version,
                l.UpdatedAt,
                l.DeletedAt,
                FeatureCount = db.Features.Count(f => f.LayerId == l.Id),
            })
            .ToListAsync(ct);
        return Results.Ok(
            new OverlayLayerListResponse(
                layers
                    .Select(l => new OverlayLayerSummary(
                        l.Id,
                        l.Name,
                        l.Kind,
                        StyleOf(l.Style),
                        l.NodeId,
                        l.FeatureCount,
                        l.Version,
                        l.UpdatedAt,
                        l.DeletedAt
                    ))
                    .ToList()
            )
        );
    }

    [Transactional(typeof(SpatialDbContext))]
    [WolverinePost("/api/overlays")]
    [ProducesResponseType(typeof(OverlayLayerState), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateOverlayLayerRequest request,
        SpatialDbContext db,
        IHierarchyDirectory hierarchy,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OverlaysManage, ct);
        if (
            gate
            is not GateOutcome.Allowed
            {
                Principal: Principal.User user,
                Org: var org,
                Scope: var scope
            }
        )
            return gate.ToResult();
        var name = request.Name?.Trim() ?? "";
        var kind = request.Kind?.Trim().ToLowerInvariant() ?? "";
        if (name.Length is 0 or > 200 || kind.Length is 0 or > 40)
            return ApiErrors.BadRequest("a layer needs a name and a kind");
        if (StyleText(request.Style) is var (style, styleError) && styleError is not null)
            return ApiErrors.BadRequest(styleError);

        // the anchor: the given node, or the org's root; the grant must COVER it
        var node = request.NodeId is { } nodeId
            ? await hierarchy.FindNodeAsync(nodeId, ct)
            : await hierarchy.FindRootAsync(ct);
        if (node is null)
            return Results.NotFound();
        if (!scope.Covers(node.Path))
            return Results.Forbid();

        var layer = new OverlayLayer
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Name = name,
            Kind = kind,
            Style = style,
            NodeId = node.Id,
            HierarchyId = node.HierarchyId,
            Path = new LTree(node.Path),
            CreatedBy = user.UserId,
        };
        db.Layers.Add(layer);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "overlay.layer_created",
            new
            {
                layer.Id,
                layer.Name,
                layer.Kind,
                layer.NodeId,
            }
        );
        return Results.Ok(new OverlayLayerState(layer.Id, layer.Version, layer.DeletedAt));
    }

    [Transactional(typeof(SpatialDbContext))]
    [WolverinePost("/api/overlays/{id}")]
    [ProducesResponseType(typeof(OverlayLayerState), StatusCodes.Status200OK)]
    public static async Task<IResult> Update(
        Guid id,
        UpdateOverlayLayerRequest request,
        SpatialDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OverlaysManage, ct);
        if (
            gate
            is not GateOutcome.Allowed
            {
                Principal: Principal.User user,
                Org: var org,
                Scope: var scope
            }
        )
            return gate.ToResult();
        var layer = await db.Layers.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layer is null || !scope.Covers(layer.Path.ToString()))
            return Results.NotFound();
        if (request.Name is { } newName)
        {
            var name = newName.Trim();
            if (name.Length is 0 or > 200)
                return ApiErrors.BadRequest("a layer needs a name");
            layer.Name = name;
        }
        if (request.Style is { } newStyle)
        {
            var (style, styleError) = StyleText(newStyle);
            if (styleError is not null)
                return ApiErrors.BadRequest(styleError);
            layer.Style = style;
        }
        layer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "overlay.layer_updated",
            new { layer.Id, layer.Name }
        );
        return Results.Ok(new OverlayLayerState(layer.Id, layer.Version, layer.DeletedAt));
    }

    /// <summary>
    /// The upload IS the feature set: rows are replaced wholesale, inside the
    /// layer's transaction, and the version bumps so cached tiles fall away.
    /// </summary>
    [Transactional(typeof(SpatialDbContext))]
    [WolverinePut("/api/overlays/{id}/features")]
    [ProducesResponseType(typeof(OverlayFeaturesReplaced), StatusCodes.Status200OK)]
    public static async Task<IResult> ReplaceFeatures(
        Guid id,
        OverlayFeaturesUpload upload,
        SpatialDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OverlaysManage, ct);
        if (
            gate
            is not GateOutcome.Allowed
            {
                Principal: Principal.User user,
                Org: var org,
                Scope: var scope
            }
        )
            return gate.ToResult();
        var layer = await db.Layers.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layer is null || !scope.Covers(layer.Path.ToString()))
            return Results.NotFound();

        var (features, error) = GeoJsonFeatures.Parse(upload.GeoJson);
        if (error is not null)
            return Results.BadRequest(new { error });

        await db.Features.Where(f => f.LayerId == layer.Id).ExecuteDeleteAsync(ct);
        foreach (var feature in features)
            db.Features.Add(
                new OverlayFeature
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    LayerId = layer.Id,
                    Geom = feature.Geometry,
                    Properties = feature.Properties,
                    // stamped from the layer's anchor at write time (ADR 2/4)
                    HierarchyId = layer.HierarchyId,
                    Path = layer.Path,
                }
            );
        layer.Version++;
        layer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "overlay.features_replaced",
            new
            {
                layer.Id,
                layer.Name,
                Count = features.Count,
            }
        );
        return Results.Ok(new OverlayFeaturesReplaced(layer.Id, features.Count, layer.Version));
    }

    [Transactional(typeof(SpatialDbContext))]
    [WolverineDelete("/api/overlays/{id}")]
    [ProducesResponseType(typeof(OverlayLayerState), StatusCodes.Status200OK)]
    public static async Task<IResult> Delete(
        Guid id,
        SpatialDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OverlaysManage, ct);
        if (
            gate
            is not GateOutcome.Allowed
            {
                Principal: Principal.User user,
                Org: var org,
                Scope: var scope
            }
        )
            return gate.ToResult();
        var layer = await db.Layers.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layer is null || !scope.Covers(layer.Path.ToString()))
            return Results.NotFound();
        // tier 2: into the trash, features and all; restore brings both back
        layer.DeletedAt = DateTimeOffset.UtcNow;
        layer.UpdatedAt = layer.DeletedAt.Value;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "overlay.layer_deleted",
            new { layer.Id, layer.Name }
        );
        return Results.Ok(new OverlayLayerState(layer.Id, layer.Version, layer.DeletedAt));
    }

    [Transactional(typeof(SpatialDbContext))]
    [WolverinePost("/api/overlays/{id}/restore")]
    [ProducesResponseType(typeof(OverlayLayerState), StatusCodes.Status200OK)]
    public static async Task<IResult> Restore(
        Guid id,
        SpatialDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OverlaysManage, ct);
        if (
            gate
            is not GateOutcome.Allowed
            {
                Principal: Principal.User user,
                Org: var org,
                Scope: var scope
            }
        )
            return gate.ToResult();
        var layer = await db
            .Layers.IgnoreQueryFilters([ModuleDbContext.SoftDeleteFilter])
            .FirstOrDefaultAsync(l => l.Id == id && l.DeletedAt != null, ct);
        if (layer is null || !scope.Covers(layer.Path.ToString()))
            return Results.NotFound();
        layer.DeletedAt = null;
        layer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "overlay.layer_restored",
            new { layer.Id, layer.Name }
        );
        return Results.Ok(new OverlayLayerState(layer.Id, layer.Version, layer.DeletedAt));
    }

    private const int MaxStyleBytes = 4096;

    /// <summary>Style is opaque JSON the map reads; it must be an object and small.</summary>
    private static (string? Style, string? Error) StyleText(JsonElement? style)
    {
        if (style is not { } s || s.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return (null, null);
        if (s.ValueKind != JsonValueKind.Object)
            return (null, "style must be a JSON object");
        var text = s.GetRawText();
        return text.Length > MaxStyleBytes ? (null, "style is too large") : (text, null);
    }

    private static JsonElement? StyleOf(string? style) =>
        style is null ? null : JsonSerializer.Deserialize<JsonElement>(style);
}
