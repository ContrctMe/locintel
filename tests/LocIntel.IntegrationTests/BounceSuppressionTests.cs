using System.Net;
using System.Net.Http.Json;
using LocIntel.Platform.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public sealed class BounceFixture : ApiFixture
{
    public const string Token = "bounce-secret";

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting("Notifications:BounceToken", Token);
    }
}

/// <summary>
/// ADR 32's bounce half: the provider-neutral intake feeds the suppression
/// list, the transport decorator drops suppressed sends instead of
/// dead-lettering them forever, and issuance tells the human first.
/// </summary>
public class BounceSuppressionTests(BounceFixture fixture) : IClassFixture<BounceFixture>
{
    private static HttpRequestMessage Report(string email, string? token = BounceFixture.Token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/notifications/bounce")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { email, reason = "bounce" }),
        };
        if (token is not null)
            request.Headers.Add("X-Bounce-Token", token);
        return request;
    }

    [Fact]
    public async Task Bounce_report_suppresses_and_issuance_tells_the_human()
    {
        var guest = fixture.GuestClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await guest.SendAsync(Report("dead@example.com", token: "wrong"))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await guest.SendAsync(Report("dead@example.com"))).StatusCode
        );
        // idempotent re-report
        Assert.Equal(
            HttpStatusCode.Accepted,
            (await guest.SendAsync(Report("dead@example.com"))).StatusCode
        );

        var member = await fixture.LoginAsync(ApiFixture.UserA);
        var blocked = await member.PostAsJsonAsync(
            "/contact-links",
            new { email = "dead@example.com" }
        );
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        var fine = await member.PostAsJsonAsync(
            "/contact-links",
            new { email = "alive@example.com" }
        );
        Assert.True(fine.IsSuccessStatusCode, await fine.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Transport_decorator_drops_suppressed_sends()
    {
        var guest = fixture.GuestClient();
        (await guest.SendAsync(Report("void@example.com"))).EnsureSuccessStatusCode();

        var transport = fixture.Factory.Services.GetRequiredService<INotificationTransport>();
        var catcher = fixture.Factory.Services.GetRequiredService<LocalMailCatcher>();
        await transport.SendAsync(new EmailMessage("void@example.com", "swallowed", "body"));
        await transport.SendAsync(new EmailMessage("fresh@example.com", "delivered", "body"));

        Assert.DoesNotContain(catcher.Sent, m => m.To == "void@example.com");
        Assert.Contains(catcher.Sent, m => m.To == "fresh@example.com");
    }

    [Fact]
    public async Task Operator_lists_and_unsuppresses_and_sending_resumes()
    {
        var guest = fixture.GuestClient();
        (await guest.SendAsync(Report("comeback@example.com"))).EnsureSuccessStatusCode();

        var member = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (
                await member.PostAsJsonAsync(
                    "/contact-links",
                    new { email = "comeback@example.com" }
                )
            ).StatusCode
        );

        // the knob the 422 promises: operator finds it and lifts it
        var op = await fixture.OperatorClient();
        var rows = await op.GetFromJsonAsync<System.Text.Json.JsonElement>(
            "/api/operator/suppressions?q=comeback"
        );
        var suppression = rows.EnumerateArray().Single();
        Assert.Equal("bounce", suppression.GetProperty("reason").GetString());
        (
            await op.DeleteAsync(
                $"/api/operator/suppressions/{suppression.GetProperty("id").GetGuid()}"
            )
        ).EnsureSuccessStatusCode();

        var resumed = await member.PostAsJsonAsync(
            "/contact-links",
            new { email = "comeback@example.com" }
        );
        Assert.True(resumed.IsSuccessStatusCode, await resumed.Content.ReadAsStringAsync());

        // tenants hold no custody over the platform-global list
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await member.GetAsync("/api/operator/suppressions")).StatusCode
        );
    }
}

public class BounceIntakeDisabledTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Intake_is_404_until_a_token_is_configured()
    {
        var response = await fixture
            .GuestClient()
            .PostAsJsonAsync("/notifications/bounce", new { email = "x@example.com" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
