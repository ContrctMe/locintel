using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Sites;

public enum SiteAttributeType
{
    Text,
    Number,
    Boolean,
}

/// <summary>
/// An ORG-defined site field (ADR 46): the tenant's data model, not the
/// template's - "drive-thru", "store manager", "cost center". Definitions
/// drive validation and the console form; values live in the site row's
/// jsonb. Public controls whether the value reaches the public site page
/// (a cost center is internal; parking is not). Deletion tier 3: config -
/// deleting a definition strips its values from every site.
/// </summary>
public sealed class SiteAttributeDefinition : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }

    /// <summary>Stable slug key ([a-z0-9_]) - the jsonb key and API name; the label is for humans.</summary>
    public required string Key { get; init; }

    public required string Label { get; set; }
    public required SiteAttributeType Type { get; init; }

    /// <summary>Exposed on the public site page (and locator surfaces) when true; internal otherwise.</summary>
    public bool Public { get; set; }

    /// <summary>UTC instant (ADR 26).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
