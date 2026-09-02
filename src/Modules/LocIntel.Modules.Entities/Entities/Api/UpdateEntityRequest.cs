namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record UpdateEntityRequest(
    string DisplayName,
    string[]? Aliases,
    Dictionary<string, string>? Descriptors,
    string? Summary,
    DateTimeOffset ExpiresAt
);
