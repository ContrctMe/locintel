using LocIntel.Platform.Auth;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Internal message (ADR 41): one verified directory-sync event, tenanted via
/// the envelope by the webhook endpoint. Email is the identity join key.
/// </summary>
public sealed record DirectoryUserSynced(DirectorySyncKind Kind, string Email, string? Name);
