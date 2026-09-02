using LocIntel.Platform.Auth;
using Microsoft.Extensions.Options;
using WorkOS;

namespace LocIntel.Integrations.WorkOS;

public sealed class WorkOSOptions
{
    public required string ApiKey { get; set; }
    public required string ClientId { get; set; }

    /// <summary>
    /// Override to point at the WorkOS emulator (@workos/emulate) for local
    /// dev and adapter smoke tests - e.g. http://localhost:4100 with the
    /// emulator's sk_test_default key. Null = the real https://api.workos.com.
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>
    /// Signing secret for inbound WorkOS webhooks (directory sync, ADR 41).
    /// Null disables webhook consumption - deliveries are rejected unverified.
    /// </summary>
    public string? WebhookSecret { get; set; }
}

/// <summary>
/// The built-in WorkOS implementation of the auth seam (ADR 14): AuthKit
/// hosted UI for authentication, org directory capability for tenant
/// lifecycle. Grants, entitlements, and audit stay internal (ADRs 6/10/12) -
/// WorkOS is identity and org directory only.
/// </summary>
public sealed class WorkOSAuthProvider
    : IAuthProvider,
        IOrganizationDirectory,
        IUserProvisioning,
        IUserLifecycle,
        IAdminPortal,
        IDirectoryEventSource
{
    private readonly WorkOSClient _client;
    private readonly string _clientId;
    private readonly string _baseUrl;
    private readonly string? _webhookSecret;

    public WorkOSAuthProvider(IOptions<WorkOSOptions> options)
    {
        _clientId = options.Value.ClientId;
        _baseUrl = options.Value.ApiBaseUrl ?? "https://api.workos.com";
        _webhookSecret = options.Value.WebhookSecret;
        var clientOptions = new global::WorkOS.WorkOSOptions
        {
            ApiKey = options.Value.ApiKey,
            ClientId = options.Value.ClientId,
        };
        if (options.Value.ApiBaseUrl is { } baseUrl)
            clientOptions.ApiBaseURL = baseUrl;
        _client = new WorkOSClient(clientOptions);
    }

    public string Name => "workos";

    public string BuildAuthorizationUrl(
        string redirectUri,
        string state,
        string? loginHint = null,
        string? orgHint = null,
        string? screenHint = null
    ) =>
        AuthorizationUrlBuilder.BuildAuthKitAuthorizationUrl(
            _baseUrl,
            _clientId,
            new AuthKitAuthorizationUrlOptions
            {
                RedirectUri = redirectUri,
                State = state,
                LoginHint = loginHint,
                OrganizationId = orgHint,
                ScreenHint = screenHint, // "sign-up" sends AuthKit to registration
            }
        );

    public async Task<ExternalIdentity> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        var response = await _client.UserManagement.AuthenticateWithCodeAsync(
            new AuthenticateWithCodeOptions { Code = code },
            cancellationToken: ct
        );
        var user = response.User;
        var name = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))
        );
        return new ExternalIdentity(
            Provider: Name,
            Subject: user.Id,
            Email: user.Email,
            Name: string.IsNullOrWhiteSpace(name) ? null : name,
            ExternalOrgId: response.OrganizationId
        );
    }

    public async Task<string> EnsureUserAsync(string email, CancellationToken ct = default)
    {
        try
        {
            var created = await _client.UserManagement.CreateAsync(
                new UserManagementCreateOptions { Email = email, EmailVerified = true },
                cancellationToken: ct
            );
            return created.Id;
        }
        catch (ApiException) // already exists: sign-up is idempotent
        {
            var existing = await _client.UserManagement.ListAsync(
                new UserManagementListOptions { Email = email },
                cancellationToken: ct
            );
            return (
                existing.Data
                ?? throw new InvalidOperationException(
                    $"user {email} vanished between create-conflict and lookup"
                )
            )[0].Id;
        }
    }

    public async Task<Uri> GeneratePortalLinkAsync(
        string externalOrgId,
        AdminPortalIntent intent,
        string returnUrl,
        CancellationToken ct = default
    )
    {
        var link = await _client.AdminPortal.GenerateLinkAsync(
            new AdminPortalGenerateLinkOptions
            {
                Organization = externalOrgId,
                Intent =
                    intent == AdminPortalIntent.DirectorySync
                        ? GenerateLinkIntent.Dsync
                        : GenerateLinkIntent.SSO,
                ReturnUrl = returnUrl,
            },
            cancellationToken: ct
        );
        return new Uri(link.Link);
    }

    public Task<DirectoryWebhook> ParseDirectoryWebhookAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default
    )
    {
        if (
            _webhookSecret is null
            || !headers.TryGetValue("WorkOS-Signature", out var signature)
            || string.IsNullOrEmpty(signature)
        )
            return Task.FromResult(new DirectoryWebhook(Verified: false, Event: null));
        try
        {
            new WebhookService().VerifyHeader(body, signature, _webhookSecret, 300);
        }
        catch (Exception)
        {
            return Task.FromResult(new DirectoryWebhook(Verified: false, Event: null));
        }

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var root = doc.RootElement;
        var eventType = root.GetProperty("event").GetString();
        if (eventType is not ("dsync.user.created" or "dsync.user.updated" or "dsync.user.deleted"))
            return Task.FromResult(new DirectoryWebhook(Verified: true, Event: null));

        var data = root.GetProperty("data");
        // a directory not linked to an organization has nothing to map to
        if (
            !data.TryGetProperty("organization_id", out var orgProp)
            || orgProp.GetString() is not { } externalOrgId
        )
            return Task.FromResult(new DirectoryWebhook(Verified: true, Event: null));

        var email = PrimaryEmail(data);
        if (email is null)
            return Task.FromResult(new DirectoryWebhook(Verified: true, Event: null));

        var inactive =
            data.TryGetProperty("state", out var state) && state.GetString() == "inactive";
        var kind =
            eventType == "dsync.user.deleted" || inactive
                ? DirectorySyncKind.UserRemoved
                : DirectorySyncKind.UserUpserted;
        var name = string.Join(
            ' ',
            new[] { GetString(data, "first_name"), GetString(data, "last_name") }.Where(s =>
                !string.IsNullOrWhiteSpace(s)
            )
        );
        return Task.FromResult(
            new DirectoryWebhook(
                Verified: true,
                Event: new DirectorySyncEvent(
                    externalOrgId,
                    kind,
                    email,
                    string.IsNullOrWhiteSpace(name) ? null : name
                )
            )
        );
    }

    private static string? PrimaryEmail(System.Text.Json.JsonElement data)
    {
        if (data.TryGetProperty("emails", out var emails))
            foreach (var entry in emails.EnumerateArray())
                if (
                    entry.TryGetProperty("primary", out var primary)
                    && primary.ValueKind == System.Text.Json.JsonValueKind.True
                    && entry.TryGetProperty("value", out var value)
                )
                    return value.GetString();
        // SCIM userName is an email address for every mainstream IdP
        return GetString(data, "username");
    }

    private static string? GetString(System.Text.Json.JsonElement data, string property) =>
        data.TryGetProperty(property, out var value)
        && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    public async Task UpdateUserNameAsync(
        string externalUserId,
        string name,
        CancellationToken ct = default
    ) =>
        await _client.UserManagement.UpdateAsync(
            externalUserId,
            new UserManagementUpdateOptions { Name = name },
            cancellationToken: ct
        );

    public async Task<Uri?> BeginPasswordResetAsync(string email, CancellationToken ct = default)
    {
        var reset = await _client.UserManagement.ResetPasswordAsync(
            new UserManagementResetPasswordOptions { Email = email },
            cancellationToken: ct
        );
        return reset.PasswordResetUrl is { } url ? new Uri(url) : null;
    }

    public async Task DeleteUserAsync(string externalUserId, CancellationToken ct = default) =>
        await _client.UserManagement.DeleteAsync(externalUserId, cancellationToken: ct);

    public async Task RevokeProviderSessionsAsync(
        string externalUserId,
        CancellationToken ct = default
    )
    {
        var sessions = await _client.UserManagement.ListSessionsAsync(
            externalUserId,
            cancellationToken: ct
        );
        foreach (var session in sessions.Data ?? [])
            await _client.UserManagement.RevokeSessionAsync(
                new UserManagementRevokeSessionOptions { SessionId = session.Id },
                cancellationToken: ct
            );
    }

    public async Task DeleteOrganizationAsync(
        string externalOrgId,
        CancellationToken ct = default
    ) => await _client.Organizations.DeleteAsync(externalOrgId, cancellationToken: ct);

    public async Task UpdateOrganizationNameAsync(
        string externalOrgId,
        string name,
        CancellationToken ct = default
    ) =>
        await _client.Organizations.UpdateAsync(
            externalOrgId,
            new OrganizationsUpdateOptions { Name = name },
            cancellationToken: ct
        );

    public async Task AddMemberAsync(
        string externalOrgId,
        string externalUserId,
        CancellationToken ct = default
    ) =>
        await _client.OrganizationMembership.CreateAsync(
            new OrganizationMembershipCreateOptions
            {
                OrganizationId = externalOrgId,
                UserId = externalUserId,
            },
            cancellationToken: ct
        );

    public async Task<string> SendInvitationAsync(
        string externalOrgId,
        string email,
        CancellationToken ct = default
    )
    {
        var invitation = await _client.UserManagement.SendInvitationAsync(
            new UserManagementSendInvitationOptions
            {
                OrganizationId = externalOrgId,
                Email = email,
                ExpiresInDays = 7,
            },
            cancellationToken: ct
        );
        return invitation.Id;
    }

    public async Task<IReadOnlyList<PendingInvitation>> ListInvitationsAsync(
        string externalOrgId,
        CancellationToken ct = default
    )
    {
        var invitations = await _client.UserManagement.ListInvitationsAsync(
            new UserManagementListInvitationsOptions { OrganizationId = externalOrgId },
            cancellationToken: ct
        );
        return
        [
            .. (invitations.Data ?? []).Select(i => new PendingInvitation(
                i.Id,
                i.Email,
                i.State.ToString(),
                i.ExpiresAt
            )),
        ];
    }

    public async Task RevokeInvitationAsync(string invitationId, CancellationToken ct = default) =>
        await _client.UserManagement.RevokeInvitationAsync(invitationId, cancellationToken: ct);

    public async Task<string> CreateOrganizationAsync(string name, CancellationToken ct = default)
    {
        var org = await _client.Organizations.CreateAsync(
            new OrganizationsCreateOptions { Name = name },
            cancellationToken: ct
        );
        return org.Id;
    }
}
