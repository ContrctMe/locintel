using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Outbound webhooks (ADR 40): the org's domain-event stream, pushed to
/// subscribed endpoints with the same t/v1 HMAC scheme the template verifies
/// on its own inbound billing webhooks. Failures retry with backoff, and
/// every attempt is a tenant-visible delivery row.
/// </summary>
public class WebhookTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private sealed record Received(string Body, string Signature, string EventName);

    [Fact]
    public async Task Events_deliver_signed_filters_apply_and_failures_retry()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        var received = new List<Received>();
        var failuresToServe = 0;
        var stub = WebApplication.CreateSlimBuilder().Build();
        // ephemeral port: parallel test CLASSES each run stubs, and the
        // default :5000 collides across them (found as intermittent
        // AddressInUse failures once a second stub-using class existed)
        stub.Urls.Add("http://127.0.0.1:0");
        stub.MapPost(
            "/hooks",
            async (HttpRequest req) =>
            {
                using var reader = new StreamReader(req.Body);
                received.Add(
                    new Received(
                        await reader.ReadToEndAsync(),
                        req.Headers["X-LocIntel-Signature"].ToString(),
                        req.Headers["X-LocIntel-Event"].ToString()
                    )
                );
                if (failuresToServe > 0)
                {
                    failuresToServe--;
                    return Results.StatusCode(500);
                }
                return Results.Ok();
            }
        );
        await stub.StartAsync();
        try
        {
            // subscribe to org events only - hierarchy noise must not deliver
            var created = await owner.PostAsJsonAsync(
                "/api/webhooks",
                new
                {
                    url = $"{stub.Urls.First()}/hooks",
                    events = new[] { "org.*", "webhook.ping" },
                }
            );
            created.EnsureSuccessStatusCode();
            var body = await created.Content.ReadFromJsonAsync<JsonElement>();
            var endpointId = body.GetProperty("id").GetGuid();
            var secret = body.GetProperty("secret").GetString()!;
            Assert.StartsWith("whsec_", secret);

            // trigger a matching domain event through the real flow
            (
                await owner.PutAsJsonAsync("/api/org", new { name = "Org A Hooked" })
            ).EnsureSuccessStatusCode();

            for (var i = 0; i < 100 && received.Count == 0; i++)
                await Task.Delay(100);
            var delivery = Assert.Single(received);
            Assert.Equal("org.renamed", delivery.EventName);

            // the body says what happened; the signature proves who said it
            using var payload = JsonDocument.Parse(delivery.Body);
            Assert.Equal("org.renamed", payload.RootElement.GetProperty("event").GetString());
            Assert.Equal(
                "Org A Hooked",
                payload.RootElement.GetProperty("payload").GetProperty("to").GetString()
            );
            var parts = delivery.Signature.Split(',');
            var timestamp = parts[0]["t=".Length..];
            var expected = Convert.ToHexStringLower(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(secret),
                    Encoding.UTF8.GetBytes($"{timestamp}.{delivery.Body}")
                )
            );
            Assert.Equal($"v1={expected}", parts[1]);

            // a non-subscribed event does not deliver
            (
                await owner.PostAsJsonAsync("/contact-links", new { email = "hooked@example.com" })
            ).EnsureSuccessStatusCode();
            await Task.Delay(1000);
            Assert.Single(received);

            // failure -> retry with backoff; both attempts become rows
            received.Clear();
            failuresToServe = 1;
            (
                await owner.PostAsync($"/api/webhooks/{endpointId}/ping", null)
            ).EnsureSuccessStatusCode();
            for (var i = 0; i < 150 && received.Count < 2; i++)
                await Task.Delay(100);
            Assert.Equal(2, received.Count);

            List<JsonElement> rows = [];
            await ApiFixture.WaitUntilAsync(
                async () =>
                {
                    rows =
                    [
                        .. (
                            await owner.GetFromJsonAsync<JsonElement>(
                                $"/api/webhooks/{endpointId}/deliveries"
                            )
                        )
                            .EnumerateArray()
                            .Where(d => d.GetProperty("eventName").GetString() == "webhook.ping"),
                    ];
                    return rows.Count >= 2;
                },
                "both webhook ping deliveries to be recorded"
            );
            Assert.Contains(
                rows,
                r => !r.GetProperty("ok").GetBoolean() && r.GetProperty("attempt").GetInt32() == 1
            );
            Assert.Contains(
                rows,
                r => r.GetProperty("ok").GetBoolean() && r.GetProperty("attempt").GetInt32() == 2
            );

            // the list surfaces last-delivery health; delete cleans up
            var listed = await owner.GetFromJsonAsync<JsonElement>("/api/webhooks");
            Assert.Single(listed.EnumerateArray());
            (await owner.DeleteAsync($"/api/webhooks/{endpointId}")).EnsureSuccessStatusCode();
            Assert.Equal(
                0,
                (await owner.GetFromJsonAsync<JsonElement>("/api/webhooks")).GetArrayLength()
            );
        }
        finally
        {
            await stub.StopAsync();
        }
    }

    [Fact]
    public async Task Webhook_custody_needs_org_manage()
    {
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await viewer.GetAsync("/api/webhooks")).StatusCode
        );
    }
}
