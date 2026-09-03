using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Bulletins;

/// <summary>A member's OWN copy of a bulletin published into a share it belongs to (ADR 48); materialized by SharedBulletinOffered, withdrawn by SharedBulletinWithdrawn, deleted when the member leaves. Tier 3.</summary>
public sealed class SharedBulletinCopy : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid BulletinId { get; init; }
    public required Guid ShareId { get; init; }
    public required string ShareName { get; init; }
    public required OrgId PublisherOrgId { get; init; }
    public required string PublisherName { get; init; }
    public required SharedBulletinKind Kind { get; init; }
    public required SharedSeverity Severity { get; init; }
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public string? EntityKind { get; init; }
    public string? DisplayName { get; init; }
    public string[] Aliases { get; init; } = [];
    public string DescriptorsJson { get; init; } = "{}";
    public string[] Areas { get; init; } = [];
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
