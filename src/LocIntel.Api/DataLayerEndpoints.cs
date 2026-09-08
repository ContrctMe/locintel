using System.Security.Cryptography;
using LocIntel.Contracts;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Spatial;
using Microsoft.Extensions.Caching.Memory;

namespace LocIntel.Api;

public sealed record DataLayerStatusResponse(string Key, string Label, string Color);

public sealed record DataLayerDescriptor(
    string Name,
    string Title,
    string Description,
    string Capability,
    int MinPointZoom,
    IReadOnlyList<DataLayerStatusResponse> Statuses
);

public sealed record DataLayerListResponse(IReadOnlyList<DataLayerDescriptor> Layers);

/// <summary>
/// The data-layer registry served (ADR 50 §3-4): every <see cref="IDataLayer"/>
/// a module registered, listed for the principal that may see it and served
/// as vector tiles at one route, one gate call each. It lives in the
/// composition root because it is the one place that may see every module's
/// layers - through DI, not references. Tiles are private (scope differs per
/// principal), cheap to re-ask for (a strong ETag), and 204 where nothing is.
/// </summary>
public static class DataLayerEndpoints
{
    private const string Mvt = "application/vnd.mapbox-vector-tile";
    private static readonly TimeSpan ClusterTileTtl = TimeSpan.FromSeconds(60);

    private static string ClusterTileKey(string layer, DataLayerTileRequest r) =>
        $"tile:{layer}:{r.Org.Value}:{r.Z}/{r.X}/{r.Y}:{r.UnderPath}:{(r.ScopePaths is null ? "*" : string.Join("|", r.ScopePaths))}";

    public static void MapDataLayerEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/map/layers",
                async (
                    IEnumerable<IDataLayer> layers,
                    IPrincipalAccessor accessor,
                    IScopeResolver scopes,
                    CancellationToken ct
                ) =>
                {
                    var visible = new List<DataLayerDescriptor>();
                    foreach (var layer in layers)
                    {
                        // a layer the principal cannot read is simply not offered
                        if (!await scopes.CanAsync(accessor.Current, layer.Capability, ct))
                            continue;
                        visible.Add(
                            new DataLayerDescriptor(
                                layer.Name,
                                layer.Title,
                                layer.Description,
                                layer.Capability,
                                layer.MinPointZoom,
                                layer
                                    .Statuses.Select(s => new DataLayerStatusResponse(
                                        s.Key,
                                        s.Label,
                                        s.Color
                                    ))
                                    .ToList()
                            )
                        );
                    }
                    return Results.Ok(new DataLayerListResponse(visible));
                }
            )
            .Produces<DataLayerListResponse>();

        app.MapGet(
                "/api/tiles/{layer}/{z:int}/{x:int}/{y:int}",
                async (
                    string layer,
                    int z,
                    int x,
                    int y,
                    Guid? under,
                    HttpContext http,
                    IEnumerable<IDataLayer> layers,
                    IHierarchyDirectory hierarchy,
                    IPrincipalAccessor accessor,
                    IScopeResolver scopes,
                    IMemoryCache cache,
                    CancellationToken ct
                ) =>
                {
                    var registered = layers.FirstOrDefault(l => l.Name == layer);
                    if (registered is null)
                        return Results.NotFound();
                    if (!DataLayerTiles.InRange(z, x, y))
                        return ApiErrors.BadRequest("tile out of range");

                    var gate = await Gate.RequireAsync(accessor, scopes, registered.Capability, ct);
                    if (gate is not GateOutcome.Allowed { Org: var org, Scope: var scope })
                        return gate.ToResult();

                    // scope never fails: a principal with no scope sees an empty map
                    IReadOnlyList<string>? paths = scope switch
                    {
                        NodeScope.EntireOrg => null,
                        NodeScope.Subtrees limited => limited.Paths,
                        _ => [],
                    };

                    // the console's chosen scope node (ADR 49's `under`), inside the grant
                    string? underPath = null;
                    if (under is { } nodeId)
                    {
                        var node = await hierarchy.FindNodeAsync(nodeId, ct);
                        if (node is null || !scope.Covers(node.Path))
                            return Results.NoContent();
                        underPath = node.Path;
                    }

                    var request = new DataLayerTileRequest(z, x, y, org, paths, underPath);
                    // a cluster tile aggregates every point in scope however far out the
                    // map is: rendered once a minute per org, scope and node, shared by
                    // everyone who sees the same rows - the org is in the key, never ambient
                    var bytes =
                        z < registered.MinPointZoom
                            ? await cache.GetOrCreateAsync(
                                ClusterTileKey(registered.Name, request),
                                async entry =>
                                {
                                    entry.AbsoluteExpirationRelativeToNow = ClusterTileTtl;
                                    return await registered.RenderAsync(request, ct);
                                }
                            ) ?? []
                            : await registered.RenderAsync(request, ct);
                    if (bytes.Length == 0)
                        return Results.NoContent();

                    var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"";
                    http.Response.Headers.CacheControl = "private, max-age=60";
                    http.Response.Headers.ETag = etag;
                    if (http.Request.Headers.IfNoneMatch.Any(v => v == etag))
                        return Results.StatusCode(StatusCodes.Status304NotModified);
                    return Results.Bytes(bytes, Mvt);
                }
            )
            .Produces<byte[]>(StatusCodes.Status200OK, Mvt)
            .Produces(StatusCodes.Status204NoContent);
    }
}
