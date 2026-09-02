namespace LocIntel.Modules.Entities.Entities.Api;

/// <summary>Links are the ones the reader may see; Grants appear for managers only.</summary>
public sealed record EntityDetail(
    Guid Id,
    EntityKind Kind,
    EntityStatus Status,
    string DisplayName,
    string[] Aliases,
    Dictionary<string, string> Descriptors,
    string Summary,
    DateTimeOffset ExpiresAt,
    bool LegalHold,
    Guid CreatedBy,
    string? Creator,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<EntityLinkView> Links,
    IReadOnlyList<EntityGrantView>? Grants
);
