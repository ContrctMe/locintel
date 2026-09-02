namespace LocIntel.Platform.Auth;

/// <summary>
/// The neutral directory-sync verbs (ADR 41). Deactivation and deletion both
/// collapse to Removed - either way the IdP says this person is out.
/// </summary>
public enum DirectorySyncKind
{
    UserUpserted,
    UserRemoved,
}
