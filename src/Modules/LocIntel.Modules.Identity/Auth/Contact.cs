using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Identity.Auth;

/// <summary>
/// An identified contact of the org (ADR 7's middle tier), persisted at link
/// issuance: the revocation store the original contact-link slice deferred.
/// Deletion tier 1 (ADR 25): revocation is a lifecycle status - the row stays
/// as the auditable record of who was let in. Purged with the org.
/// </summary>
public sealed class Contact : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Email { get; init; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
