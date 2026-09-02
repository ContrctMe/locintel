namespace LocIntel.Platform.Auth;

/// <summary>
/// Optional capability (ADR 41): the provider's directory-sync (SCIM)
/// webhook, parsed behind the seam. Framework-neutral on purpose - raw body
/// plus headers in, verified neutral event out - so the endpoint stays a
/// dumb pipe and the provider's signature scheme stays the provider's.
/// </summary>
public interface IDirectoryEventSource
{
    Task<DirectoryWebhook> ParseDirectoryWebhookAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default
    );
}
