namespace LocIntel.Modules.Network.Bulletins.Api;

/// <summary>EntityId copies the record's kind, name, aliases, and descriptors into the bulletin (need-to-know applies to the publisher). ExpiresAt defaults to 30 days, max 180.</summary>
public sealed record PublishBulletinRequest(
    SharedBulletinKind Kind,
    SharedSeverity Severity,
    string Title,
    string Body,
    Guid? EntityId = null,
    string[]? Areas = null,
    DateTimeOffset? ExpiresAt = null
);
