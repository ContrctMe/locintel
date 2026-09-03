using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Bulletins;

/// <summary>
/// What the PUBLISHER owns (ADR 48): the bulletin as published into a share.
/// Every active member gets its own SharedBulletinCopy through the outbox;
/// withdrawal fans out the same way. Carries an optional entity payload
/// (kind, name, aliases, descriptors) so members can import it as their own
/// Suspected record. OrgId is the publisher. Tier 1.
/// </summary>
public sealed class SharedBulletin : IOrgScoped
{
    public required Guid Id { get; init; }
    public required Guid ShareId { get; init; }
    public required OrgId OrgId { get; init; }
    public required string PublisherName { get; init; }
    public required SharedBulletinKind Kind { get; init; }
    public required SharedSeverity Severity { get; init; }
    public required string Title { get; set; }
    public string Body { get; set; } = "";
    public string? EntityKind { get; init; }
    public string? DisplayName { get; init; }
    public string[] Aliases { get; init; } = [];
    public string DescriptorsJson { get; init; } = "{}";
    public string[] Areas { get; init; } = [];
    public Guid? SourceEntityId { get; init; }
    public required Guid PublishedBy { get; init; }
    public DateTimeOffset PublishedAt { get; init; } = DateTimeOffset.UtcNow;
    public required DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }

    public Messages.SharedBulletinOffered Offered(string shareName) =>
        new(
            Id,
            ShareId,
            shareName,
            OrgId,
            PublisherName,
            Kind,
            Severity,
            Title,
            Body,
            EntityKind,
            DisplayName,
            Aliases,
            DescriptorsJson,
            Areas,
            PublishedAt,
            ExpiresAt
        );
}
