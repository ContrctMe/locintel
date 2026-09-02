using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Bulletins;

/// <summary>
/// A COPY published into a share (blueprint: the source stays owned; the
/// copy has its own ownership and retention). Carries an optional entity
/// payload (kind, name, aliases, descriptors) so members can import it as
/// their own Suspected record. Readable by active members and the
/// publisher; only the publisher withdraws. Tier 1.
/// </summary>
public sealed class SharedBulletin
{
    public required Guid Id { get; init; }
    public required Guid ShareId { get; init; }
    public required OrgId PublisherOrgId { get; init; }
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
}
