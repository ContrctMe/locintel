namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Server-side session record: the revocation authority the self-contained
/// cookie cannot be. Every user cookie carries a session id claim; the
/// pipeline rejects cookies whose record is revoked or gone. Deletion tier 3
/// (ADR 25): ephemera, hard-deleted with the account. Platform-global like
/// all identity tables (a session exists before any org context does).
/// CreatedAt/RevokedAt are UTC instants (timestamptz, ADR 26).
/// </summary>
public sealed class UserSession
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>Truncated browser handle so a human can tell their sessions apart.</summary>
    public string? UserAgent { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
