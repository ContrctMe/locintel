namespace LocIntel.Platform.Auth;

/// <summary>
/// One provider-neutral directory event (ADR 41). Email is the identity join
/// key: SCIM events carry directory user ids while login carries the
/// provider's user-management ids, and email is the only stable bridge.
/// </summary>
public sealed record DirectorySyncEvent(
    string ExternalOrgId,
    DirectorySyncKind Kind,
    string Email,
    string? Name
);
