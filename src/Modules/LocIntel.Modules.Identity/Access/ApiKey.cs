using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Identity.Access;

/// <summary>
/// A server-to-server credential (ADR 40): the SECRET is shown once and only
/// its SHA-256 lives here. A key acts as a service principal holding exactly
/// one role (optionally subtree-scoped) - the same grant model as people, so
/// the three gates need nothing new. Platform-global table (allowlisted from
/// tenant RLS): a credential must be resolvable before tenant context exists,
/// the same argument as sessions. Revocation is a status flip; the row stays
/// as the auditable record.
/// </summary>
public sealed class ApiKey
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Name { get; init; }

    /// <summary>SHA-256 of the full token, hex. Never the token.</summary>
    public required string SecretHash { get; init; }

    /// <summary>First characters of the token, for humans to tell keys apart.</summary>
    public required string Prefix { get; init; }

    public required Guid RoleId { get; init; }
    public string? ScopePath { get; init; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>UTC instant (ADR 26); null = non-expiring. Rotation sets it on the OLD key: the overlap window.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
