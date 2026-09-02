using LocIntel.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The SMS seam. There is no SMS feature in the template - only the port, an
/// off default, and a dev catcher - so what is worth pinning is that the
/// default is genuinely inert and that the port resolves for a fork to
/// implement against.
/// </summary>
public class SmsTransportTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public void The_port_resolves()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<ISmsTransport>();
        // this fork's fixture sets Notifications:Sms=local so its SMS tests can
        // read texts; the inert default is pinned directly below
        Assert.IsType<LocalSmsCatcher>(transport);
    }

    [Fact]
    public async Task The_off_transport_drops_rather_than_throws()
    {
        // a texting outage must never take down the flow that wanted to text
        var transport = new NoSmsTransport(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NoSmsTransport>.Instance
        );
        await transport.SendAsync(new SmsMessage("+15551234567", "hello"));
    }

    [Fact]
    public async Task The_dev_catcher_records_instead_of_sending()
    {
        var catcher = new LocalSmsCatcher();
        await catcher.SendAsync(new SmsMessage("+15551234567", "first"));
        await catcher.SendAsync(new SmsMessage("+15559876543", "second"));

        Assert.Equal(2, catcher.Sent.Count);
        Assert.Equal("first", catcher.Sent[0].Body);
        Assert.Equal("+15559876543", catcher.Sent[1].To);
    }
}
