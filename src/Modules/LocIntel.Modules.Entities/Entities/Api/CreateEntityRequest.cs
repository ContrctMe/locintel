namespace LocIntel.Modules.Entities.Entities.Api;

/// <summary>ExpiresAt defaults to the retention floor (a year); never beyond the ceiling.</summary>
public sealed record CreateEntityRequest(
    EntityKind Kind,
    string DisplayName,
    string[]? Aliases = null,
    Dictionary<string, string>? Descriptors = null,
    string? Summary = null,
    DateTimeOffset? ExpiresAt = null
);
