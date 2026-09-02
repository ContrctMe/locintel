namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// Global identity (ADR 35: identity is global, org data is regional). Keyed to
/// the auth provider by (Provider, Subject); email is the human handle. No
/// IOrgScoped - identity tables are platform-global by design (allowlisted from
/// tenant RLS): "which orgs does this user belong to" must be answerable before
/// any org context exists.
/// </summary>
public sealed class AppUser
{
    public required Guid Id { get; init; }
    public required string Provider { get; init; }
    public required string Subject { get; init; }
    public required string Email { get; set; }
    public string? Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static AppUser Create(string provider, string subject, string email, string? name) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Provider = provider,
            Subject = subject,
            Email = email,
            Name = name,
        };
}
