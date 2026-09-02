using Microsoft.EntityFrameworkCore;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Sites;

/// <summary>
/// The physical location (ADR 3): rich attributes a hierarchy node never has.
/// Path is denormalized from the parent node so scope stays one ltree prefix
/// predicate (IPathScoped). TimeZone is REQUIRED and IANA - every temporal
/// feature hangs off it (ADR 26). Deletion tier 1: a site closes or
/// relocates; it is never deleted.
/// </summary>
public sealed class Site : IPathScoped
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
