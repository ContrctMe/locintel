namespace LocIntel.Modules.Network.Bulletins.Api;

public sealed record SharedBulletinView(
    Guid Id,
    Guid ShareId,
    string ShareName,
    Guid PublisherOrgId,
    string PublisherName,
    bool Mine,
    SharedBulletinKind Kind,
    SharedSeverity Severity,
    string Title,
    string Body,
    string? EntityKind,
    string? DisplayName,
    string[] Aliases,
    Dictionary<string, string> Descriptors,
    string[] Areas,
    DateTimeOffset PublishedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? WithdrawnAt,
    bool Active
);
