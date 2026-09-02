using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Self-serve closure (operability item 7): request -> 30-day grace with
/// everything still working and any manager able to cancel -> the sweep
/// offboards after the window. Deliberate by construction.
/// </summary>
public class OrgClosureTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient Client, Guid OrgId)> FoundOrgAsync(string email, string slug)
    {
        var owner = await fixture.LoginAsync(email);
        var created = await owner.PostAsJsonAsync(
            "/api/orgs",
            new { name = $"Closing {slug}", slug }
        );
        created.EnsureSuccessStatusCode();
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orgId")
            .GetGuid();
        for (var i = 0; i < 100; i++)
        {
            var me = await owner.GetFromJsonAsync<JsonElement>("/me");
            if (me.GetProperty("organizations").GetArrayLength() > 0)
                break;
            await Task.Delay(100);
        }
        (await owner.PostAsJsonAsync("/auth/switch-org", new { orgId })).EnsureSuccessStatusCode();
        return (owner, orgId);
    }

    [Fact]
    public async Task Request_notifies_managers_and_cancel_unwinds()
    {
        var (owner, _) = await FoundOrgAsync("closer@locintel.local", "close-cancel");
        var requested = await owner.PostAsync("/api/org/close", null);
        Assert.True(requested.IsSuccessStatusCode, await requested.Content.ReadAsStringAsync());

        var status = await owner.GetFromJsonAsync<JsonElement>("/api/org/closure");
        Assert.NotEqual(JsonValueKind.Null, status.GetProperty("purgesAt").ValueKind);

        // every manager is told, with the date and the cancel path
        var catcher = fixture.Factory.Services.GetRequiredService<LocalMailCatcher>();
        EmailMessage? mail = null;
        for (var i = 0; i < 200 && mail is null; i++)
        {
            mail = catcher.Sent.FirstOrDefault(m =>
                m.To == "closer@locintel.local" && m.Subject.Contains("scheduled to close")
            );
            if (mail is null)
                await Task.Delay(100);
        }
        Assert.NotNull(mail);
        Assert.Contains("permanently deleted", mail!.TextBody);

        // grace window: everything still works
        (await owner.GetAsync("/api/members")).EnsureSuccessStatusCode();

        (await owner.PostAsync("/api/org/close/cancel", null)).EnsureSuccessStatusCode();
        var after = await owner.GetFromJsonAsync<JsonElement>("/api/org/closure");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("requestedAt").ValueKind);
    }

    [Fact]
    public async Task Sweep_offboards_only_after_the_window_closes()
    {
        var (owner, orgId) = await FoundOrgAsync("sweeper@locintel.local", "close-sweep");
        (await owner.PostAsync("/api/org/close", null)).EnsureSuccessStatusCode();

        // sweep runs, window still open: nothing happens
        await PublishClosureAsync(orgId);
        await Task.Delay(500);
        (await owner.GetAsync("/api/org/closure")).EnsureSuccessStatusCode();

        // backdate past the window, sweep again: the org offboards
        await using (var conn = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE tenancy.organizations SET close_requested_at = now() - interval '31 days' WHERE id = $1",
                conn
            );
            cmd.Parameters.AddWithValue(orgId);
            await cmd.ExecuteNonQueryAsync();
        }
        await PublishClosureAsync(orgId);

        // enforcement follows the read model: requests into the org stop
        var blocked = false;
        for (var i = 0; i < 200 && !blocked; i++)
        {
            blocked = (await owner.GetAsync("/api/members")).StatusCode == HttpStatusCode.Forbidden;
            if (!blocked)
                await Task.Delay(100);
        }
        Assert.True(blocked, await fixture.DeadLetterSummary());
    }

    private async Task PublishClosureAsync(Guid orgId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
        await bus.PublishAsync(
            new LocIntel.Modules.Tenancy.Organizations.ProcessOrgClosure(),
            new Wolverine.DeliveryOptions { TenantId = orgId.ToString() }
        );
    }
}
