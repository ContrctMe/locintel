using System.Text;
using LocIntel.Platform.Auth;

namespace LocIntel.Modules.Identity.Auth;

/// <summary>
/// Dev/test-only IAuthProvider (ADR 14's base contract, nothing more): the
/// "authorization server" is the redirect itself - the code IS the login hint.
/// Lets the template run and its test suites authenticate with no external
/// account. Program blocks it in Production.
/// </summary>
public sealed class LocalAuthProvider
    : IAuthProvider,
        IOrganizationDirectory,
        IUserProvisioning,
        IUserLifecycle
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        List<PendingInvitation>
    > _invitations = new();

    // MUST match the subject ExchangeCodeAsync mints for the same person -
    // they disagreed ($"local_{email}" here vs the bare email at login), so a
    // provisioned or invited user could never match their own login and got a
    // second, org-less account instead.
    public Task<string> EnsureUserAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(email);

    public Task<string> CreateOrganizationAsync(string name, CancellationToken ct = default) =>
        Task.FromResult($"local_org_{Guid.CreateVersion7():N}");

    public Task UpdateUserNameAsync(
        string externalUserId,
        string name,
        CancellationToken ct = default
    ) => Task.CompletedTask;

    public Task<Uri?> BeginPasswordResetAsync(string email, CancellationToken ct = default) =>
        Task.FromResult<Uri?>(
            new Uri($"http://localhost/reset?email={Uri.EscapeDataString(email)}")
        );

    public Task DeleteUserAsync(string externalUserId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RevokeProviderSessionsAsync(
        string externalUserId,
        CancellationToken ct = default
    ) => Task.CompletedTask;

    public Task DeleteOrganizationAsync(string externalOrgId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task UpdateOrganizationNameAsync(
        string externalOrgId,
        string name,
        CancellationToken ct = default
    ) => Task.CompletedTask;

    public Task AddMemberAsync(
        string externalOrgId,
        string externalUserId,
        CancellationToken ct = default
    ) => Task.CompletedTask;

    public Task<string> SendInvitationAsync(
        string externalOrgId,
        string email,
        CancellationToken ct = default
    )
    {
        var id = $"local_invite_{Guid.CreateVersion7():N}";
        _invitations
            .GetOrAdd(externalOrgId, _ => [])
            .Add(new PendingInvitation(id, email, "pending", DateTimeOffset.UtcNow.AddDays(7)));
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<PendingInvitation>> ListInvitationsAsync(
        string externalOrgId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<PendingInvitation>>(
            _invitations.GetValueOrDefault(externalOrgId) ?? []
        );

    public Task RevokeInvitationAsync(string invitationId, CancellationToken ct = default)
    {
        foreach (var list in _invitations.Values)
            list.RemoveAll(i => i.Id == invitationId);
        return Task.CompletedTask;
    }

    public string Name => "local";

    public string BuildAuthorizationUrl(
        string redirectUri,
        string state,
        string? loginHint = null,
        string? orgHint = null,
        string? screenHint = null
    )
    {
        var email = loginHint ?? "dev@locintel.local";
        var code = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}|{orgHint}"));
        return $"{redirectUri}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
    }

    public Task<ExternalIdentity> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(code)).Split('|', 2);
        var email = parts[0];
        var orgHint = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
        return Task.FromResult(
            new ExternalIdentity(
                Name,
                Subject: email,
                Email: email,
                Name: email.Split('@')[0],
                ExternalOrgId: orgHint
            )
        );
    }
}
