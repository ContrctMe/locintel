using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Bulletins.Messages;

/// <summary>Fanned out by the publisher to the share's active members: each materializes its own copy.</summary>
public sealed record SharedBulletinOffered(
    Guid BulletinId,
    Guid ShareId,
    string ShareName,
    OrgId PublisherOrgId,
    string PublisherName,
    SharedBulletinKind Kind,
    SharedSeverity Severity,
    string Title,
    string Body,
    string? EntityKind,
    string? DisplayName,
    string[] Aliases,
    string DescriptorsJson,
    string[] Areas,
    DateTimeOffset PublishedAt,
    DateTimeOffset ExpiresAt
);
