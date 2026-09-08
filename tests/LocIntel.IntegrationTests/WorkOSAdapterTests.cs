using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Smoke-tests the REAL WorkOSAuthProvider (ADR 14) against @workos/emulate -
/// the same adapter code path production uses, exchanging codes over HTTP with
/// a faithful API implementation. Non-interactive mode auto-issues the code,
/// so the whole AuthKit dance runs headless.
/// </summary>
public class WorkOSEmulatorFixture : ApiFixture
{
    private IContainer _emulator = null!;
    public string EmulatorUrl { get; private set; } = null!;

    private const string Seed = """
        users:
          - id: user_01TESTALICE0000000000000
            email: alice@acme.test
            first_name: Alice
            last_name: Test
            email_verified: true
          - id: user_01SECURITYSESSION000000
            email: session-security@smoke.test
            first_name: Security
            last_name: Session
            email_verified: true
        """;

    public override async Task InitializeAsync()
    {
        _emulator = new ContainerBuilder("ghcr.io/workos/emulate:latest")
            .WithPortBinding(4100, assignRandomHostPort: true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(Seed), "/app/workos-emulate.config.yaml")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r.ForPath("/health").ForPort(4100))
            )
            .Build();
        await _emulator.StartAsync();
        EmulatorUrl = $"http://{_emulator.Hostname}:{_emulator.GetMappedPublicPort(4100)}";
        await base.InitializeAsync();
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Auth:Provider", "workos");
        builder.UseSetting("Auth:WorkOS:ApiKey", "sk_test_default");
        builder.UseSetting("Auth:WorkOS:ClientId", "client_locintel_test");
        builder.UseSetting("Auth:WorkOS:ApiBaseUrl", EmulatorUrl);
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _emulator.DisposeAsync();
    }
}

public class WorkOSAdapterTests(WorkOSEmulatorFixture fixture)
    : IClassFixture<WorkOSEmulatorFixture>
{
    [Fact]
    public async Task User_lifecycle_capability_runs_against_the_emulator()
    {
        var lifecycle = (LocIntel.Platform.Auth.IUserLifecycle)
            fixture.Factory.Services.GetRequiredService<LocIntel.Platform.Auth.IAuthProvider>();
        var provisioning = (LocIntel.Platform.Auth.IUserProvisioning)
            fixture.Factory.Services.GetRequiredService<LocIntel.Platform.Auth.IAuthProvider>();

        await provisioning.EnsureUserAsync("lifecycle@smoke.test");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", "sk_test_default");
        var users = await http.GetFromJsonAsync<JsonElement>(
            $"{fixture.EmulatorUrl}/user_management/users?email=lifecycle@smoke.test"
        );
        var externalId = users.GetProperty("data")[0].GetProperty("id").GetString()!;

        // rename syncs to the provider
        await lifecycle.UpdateUserNameAsync(externalId, "Smoke Renamed");

        // the provider MINTS the password reset. Real WorkOS returns the
        // reset URL; the emulator returns only the token, so null (meaning
        // "the provider handles delivery") is a valid outcome here - the
        // point is the create call round-trips.
        await lifecycle.BeginPasswordResetAsync("lifecycle@smoke.test");

        // GDPR: the provider record goes too
        await lifecycle.DeleteUserAsync(externalId);
        var after = await http.GetFromJsonAsync<JsonElement>(
            $"{fixture.EmulatorUrl}/user_management/users?email=lifecycle@smoke.test"
        );
        Assert.Equal(0, after.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Directory_capability_runs_against_the_emulator()
    {
        var provider = (LocIntel.Platform.Auth.IOrganizationDirectory)
            fixture.Factory.Services.GetRequiredService<LocIntel.Platform.Auth.IAuthProvider>();

        // org + invitation lifecycle through the REAL adapter
        var externalOrgId = await provider.CreateOrganizationAsync("Adapter Smoke Org");
        Assert.StartsWith("org_", externalOrgId);

        var invitationId = await provider.SendInvitationAsync(externalOrgId, "invitee@smoke.test");
        var pending = await provider.ListInvitationsAsync(externalOrgId);
        var row = Assert.Single(pending, i => i.Id == invitationId);
        Assert.Equal("invitee@smoke.test", row.Email);

        await provider.RevokeInvitationAsync(invitationId);
        var after = await provider.ListInvitationsAsync(externalOrgId);
        Assert.DoesNotContain(after, i => i.Id == invitationId && i.State == "pending");
    }

    [Fact]
    public async Task Admin_portal_capability_runs_against_the_emulator()
    {
        var provider =
            fixture.Factory.Services.GetRequiredService<LocIntel.Platform.Auth.IAuthProvider>();
        var directory = (LocIntel.Platform.Auth.IOrganizationDirectory)provider;
        var portal = Assert.IsAssignableFrom<LocIntel.Platform.Auth.IAdminPortal>(provider);

        var externalOrgId = await directory.CreateOrganizationAsync("Portal Smoke Org");
        var link = await portal.GeneratePortalLinkAsync(
            externalOrgId,
            LocIntel.Platform.Auth.AdminPortalIntent.SingleSignOn,
            "http://localhost/settings"
        );
        Assert.StartsWith(fixture.EmulatorUrl, link.ToString());
    }

    [Fact]
    public async Task Full_authkit_flow_through_real_adapter()
    {
        // No RedirectHandler: the authorize hop leaves the in-proc test server.
        var client = fixture.Factory.CreateDefaultClient(new CookieContainerHandler());

        // 1. API -> provider authorize URL
        var login = await client.GetAsync("/auth/login?hint=alice@acme.test");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var authorizeUrl = login.Headers.Location!.ToString();
        Assert.StartsWith(fixture.EmulatorUrl, authorizeUrl);

        // 2. Emulator authorize -> callback with code (real HTTP)
        using var external = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var authorize = await external.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        var callback = authorize.Headers.Location!;
        Assert.Contains("code=", callback.Query);

        // 3. Callback: adapter exchanges the code over HTTP, session issued
        var exchanged = await client.GetAsync(callback.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, exchanged.StatusCode); // -> /me

        var me = await client.GetFromJsonAsync<JsonElement>("/me");
        Assert.Equal("user", me.GetProperty("tier").GetString());
        Assert.Equal("alice@acme.test", me.GetProperty("email").GetString());
        Assert.Equal("Alice Test", me.GetProperty("name").GetString());
    }
}
