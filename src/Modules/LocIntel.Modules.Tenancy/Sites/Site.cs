using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// The physical location (ADR 3): rich attributes a hierarchy node never has.
/// Path is denormalized from the parent node so scope stays one ltree prefix
/// predicate (IPathScoped). TimeZone is REQUIRED and IANA - every temporal
/// feature hangs off it (ADR 26). Deletion tier 1: a site closes or
/// relocates; it is never deleted.
/// </summary>
public sealed class Site : IPathIndexed, ISpatialPoint
{
    public required SiteId Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid NodeId { get; set; }
    public required string Name { get; set; }
    public required string TimeZone { get; set; }
    public required LTree Path { get; set; }

    /// <summary>Source-system id for idempotent ingest (ADR 18).</summary>
    public string? ExternalId { get; set; }

    public SiteStatus Status { get; set; } = SiteStatus.Open;

    /// <summary>
    /// Postgres xmin as the optimistic-concurrency token (operability item
    /// 5): the client echoes it on update, a mismatch is a 409 - two admins
    /// editing the same site stop silently clobbering each other.
    /// </summary>
    public uint Version { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>
    /// geography(Point, 4326) beside the doubles (ADR 50): the indexed column
    /// spatial predicates run on. DERIVED - TenancyDbContext rewrites it from
    /// Latitude/Longitude on every save, so callers never set it; the doubles
    /// stay the request/response shape.
    /// </summary>
    public Point? Location { get; internal set; }

    /// <summary>
    /// The spatial key (ADR 51): the point's zoom-20 tile as a Morton code.
    /// DERIVED like Location, so a viewport or a tile is an integer range the
    /// app role can serve from the btree under row security.
    /// </summary>
    public long? Cell { get; internal set; }

    /// <summary>
    /// Path as "C"-collated text (ADR 51): DERIVED from Path on save, so a
    /// subtree is one leakproof text range (see PathKeys).
    /// </summary>
    public string PathText { get; internal set; } = "";

    /// <summary>
    /// Org-defined attribute values (ADR 46), keyed by definition Key. Raw
    /// JSON text mapped to jsonb - endpoints validate against the org's
    /// definitions before anything lands here.
    /// </summary>
    public string AttributesJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    LTree IPathScoped.Path => Path;

    public static string Label(SiteId id) => "s" + id.Value.ToString("N");
}

[System.Text.Json.Serialization.JsonConverter(
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter)
)]
public enum SiteStatus
{
    ComingSoon,
    Open,
    TemporarilyClosed,
    Closed,
}
