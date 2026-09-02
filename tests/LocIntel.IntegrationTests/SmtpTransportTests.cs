using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The REAL SmtpNotificationTransport (ADR 32) against Mailpit - actual SMTP
/// submission over the wire, read back through the sink's API. Proves the
/// production adapter end to end: outbox handler -> MailKit -> RFC 5321.
/// </summary>
public sealed class MailpitFixture : ApiFixture
{
    private IContainer _mailpit = null!;
    public string MailpitApiUrl { get; private set; } = null!;
    private ushort _smtpPort;

    public override async Task InitializeAsync()
    {
        _mailpit = new ContainerBuilder("axllent/mailpit:latest")
            .WithPortBinding(1025, assignRandomHostPort: true)
            .WithPortBinding(8025, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(r => r.ForPath("/livez").ForPort(8025))
            )
            .Build();
        await _mailpit.StartAsync();
        _smtpPort = _mailpit.GetMappedPublicPort(1025);
        MailpitApiUrl = $"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}";
        await base.InitializeAsync();
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting("Notifications:Transport", "smtp");
        builder.UseSetting("Notifications:Smtp:Host", _mailpit.Hostname);
        builder.UseSetting("Notifications:Smtp:Port", _smtpPort.ToString());
        builder.UseSetting("Notifications:Smtp:UseStartTls", "false"); // local sink
        builder.UseSetting("Notifications:Smtp:FromAddress", "no-reply@locintel.test");
        builder.UseSetting("Notifications:Smtp:FromName", "LocIntel");
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _mailpit.DisposeAsync();
    }
}

public class SmtpTransportTests(MailpitFixture fixture) : IClassFixture<MailpitFixture>
{
    [Fact]
    public async Task Contact_link_email_arrives_over_real_smtp()
    {
        var member = await fixture.LoginAsync(ApiFixture.UserA);
        (
            await member.PostAsJsonAsync("/contact-links", new { email = "visitor@example.com" })
        ).EnsureSuccessStatusCode();

        // outbox -> handler -> MailKit -> Mailpit
        using var sink = new HttpClient();
        JsonElement message = default;
        var delivered = false;
        for (var i = 0; i < 200 && !delivered; i++)
        {
            var inbox = await sink.GetFromJsonAsync<JsonElement>(
                $"{fixture.MailpitApiUrl}/api/v1/messages"
            );
            foreach (var m in inbox.GetProperty("messages").EnumerateArray())
                if (
                    m.GetProperty("To")[0].GetProperty("Address").GetString()
                    == "visitor@example.com"
                )
                {
                    message = m;
                    delivered = true;
                }
            if (!delivered)
                await Task.Delay(100);
        }
        Assert.True(delivered, await fixture.DeadLetterSummary());
        Assert.Equal("Your Org A access link", message.GetProperty("Subject").GetString());
        Assert.Equal(
            "no-reply@locintel.test",
            message.GetProperty("From").GetProperty("Address").GetString()
        );

        // the body carries the redeem link
        var id = message.GetProperty("ID").GetString();
        var full = await sink.GetFromJsonAsync<JsonElement>(
            $"{fixture.MailpitApiUrl}/api/v1/message/{id}"
        );
        Assert.Contains("/contact/redeem?token=", full.GetProperty("Text").GetString());
    }
}
