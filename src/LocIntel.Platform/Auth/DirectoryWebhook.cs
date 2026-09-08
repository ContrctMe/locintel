namespace LocIntel.Platform.Auth;

/// <summary>
/// Parse outcome for a directory webhook delivery (ADR 41). Verified=false
/// means the signature failed (400 - do not trust anything in the body);
/// Verified with a null Event is a genuine delivery of an event type we do
/// not consume (202 - keep the provider's retry health green).
/// </summary>
public sealed record DirectoryWebhook(
    bool Verified,
    DirectorySyncEvent? Event,
    string? RevokeUserSubject = null,
    DateTimeOffset? RevokeSessionsBefore = null
);
