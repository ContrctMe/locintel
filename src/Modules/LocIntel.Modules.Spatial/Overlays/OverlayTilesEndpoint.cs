using System.Security.Cryptography;
using LocIntel.Contracts;
using LocIntel.Modules.Spatial.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Spatial.Overlays;

/// <summary>
/// One overlay layer as vector tiles (ADR 50 §4), the same contract as the
/// sites layer: one gate call, scope inside the SQL, <c>ST_AsMVT</c>, private
/// cache with a strong ETag, 204 where nothing is. Features carry their id
/// and the properties the org uploaded, expanded from jsonb by PostGIS.
/// </summary>
public static class OverlayTilesEndpoint
{
    public const string Layer = "overlay";

    private const int Extent = 4096;
    private const int Buffer = 64;

    [Transactional(
        typeof(SpatialDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/tiles/overlays/{id}/{z}/{x}/{y}")]
    [ProducesResponseType(
        typeof(byte[]),
        StatusCodes.Status200OK,
        "application/vnd.mapbox-vector-tile"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Get(
        Guid id,
        int z,
        int x,
        int y,
        HttpContext http,
        SpatialDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        if (z is < 0 or > 22 || x < 0 || y < 0 || x >= (1L << z) || y >= (1L << z))
            return ApiErrors.BadRequest("tile out of range");

        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.OverlaysRead, ct);
        if (gate is not GateOutcome.Allowed { Org: var org, Scope: var scope })
            return gate.ToResult();

        // a layer outside the scope, in the trash, or another org's: nothing here
        var layer = await db.Layers.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (layer is null || !scope.Covers(layer.Path.ToString()))
            return Results.NoContent();

        string[]? paths = scope is NodeScope.Subtrees limited ? limited.Paths.ToArray() : null;
        var bytes = await RenderAsync(db, layer.Id, z, x, y, org, paths, ct);
        if (bytes.Length == 0)
            return Results.NoContent();

        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(bytes))[..32]}\"";
        http.Response.Headers.CacheControl = "private, max-age=60";
        http.Response.Headers.ETag = etag;
        if (http.Request.Headers.IfNoneMatch.Any(v => v == etag))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Bytes(bytes, "application/vnd.mapbox-vector-tile");
    }

    private static async Task<byte[]> RenderAsync(
        SpatialDbContext db,
        Guid layerId,
        int z,
        int x,
        int y,
        OrgId org,
        string[]? paths,
        CancellationToken ct
    )
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("z", z),
            new("x", x),
            new("y", y),
            new("org", org.Value),
            new("layer", layerId),
        };
        var predicates = "";
        if (paths is not null)
        {
            predicates += " AND f.path <@ ANY(@paths::ltree[])";
            parameters.Add(
                new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = paths,
                }
            );
        }

        var sql = $"""
            WITH bounds AS (SELECT ST_TileEnvelope(@z, @x, @y) AS geom),
            features AS (
                SELECT ST_AsMVTGeom(ST_Transform(f.geom::geometry, 3857), b.geom, {Extent}, {Buffer}, true) AS geom,
                       f.id::text AS id, f.properties
                FROM spatial.overlay_features f, bounds b
                WHERE f.org_id = @org
                  AND f.layer_id = @layer
                  AND ST_Intersects(f.geom, ST_Transform(b.geom, 4326)::geography){predicates}
            )
            SELECT COALESCE(ST_AsMVT(f, '{Layer}', {Extent}, 'geom'), ''::bytea) AS "Value"
            FROM features f
            WHERE f.geom IS NOT NULL
            """;

        return await db
            .Database.SqlQueryRaw<byte[]>(sql, parameters.Cast<object>().ToArray())
            .SingleAsync(ct);
    }
}
