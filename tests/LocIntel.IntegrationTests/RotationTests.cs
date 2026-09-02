using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Credential hygiene (operability item 3): zero-downtime rotation with an
/// overlap window on both credential types, and real expiry enforcement.
/// </summary>
public class RotationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(Guid Id, string Secret)> CreateKeyAsync(HttpClient owner, string name)
    {
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await owner.PostAsJsonAsync(
            "/api/api-keys",
            new
            {
                name,
                roleId,
                expiresInDays = 30,
            }
        );
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("secret").GetString()!);
    }

    private HttpClient ServiceClient(string secret)
    {
        var client = fixture.Factory.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        return client;
    }

    [Fact]
    public async Task Api_key_rotation_overlaps_then_the_old_key_expires()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var (id, oldSecret) = await CreateKeyAsync(owner, "rotating");

        (await ServiceClient(oldSecret).GetAsync("/api/sites")).EnsureSuccessStatusCode();

        var rotated = await owner.PostAsJsonAsync($"/api/api-keys/{id}/rotate", new { });
        Assert.True(rotated.IsSuccessStatusCode, await rotated.Content.ReadAsStringAsync());
        var body = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        var newSecret = body.GetProperty("secret").GetString()!;
        Assert.NotEqual(oldSecret, newSecret);

        // the overlap window: BOTH credentials answer
        (await ServiceClient(oldSecret).GetAsync("/api/sites")).EnsureSuccessStatusCode();
        (await ServiceClient(newSecret).GetAsync("/api/sites")).EnsureSuccessStatusCode();

        // collapse the overlap (superuser arrange - waiting 24h is not a test)
        await using (var conn = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE identity.api_keys SET expires_at = now() - interval '1 minute' WHERE id = $1",
                conn
            );
            cmd.Parameters.AddWithValue(id);
            await cmd.ExecuteNonQueryAsync();
        }
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await ServiceClient(oldSecret).GetAsync("/api/sites")).StatusCode
        );
        (await ServiceClient(newSecret).GetAsync("/api/sites")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Webhook_rotation_signs_with_both_secrets_through_the_window()
    {
        var received = new List<string>();
        var stub = WebApplication.CreateSlimBuilder().Build();
        stub.Urls.Add("http://127.0.0.1:0"); // parallel classes: never the default port
        stub.MapPost(
            "/hooks",
            async (HttpContext http) =>
            {
                using var reader = new StreamReader(http.Request.Body);
                var requestBody = await reader.ReadToEndAsync();
                lock (received)
                    received.Add($"{http.Request.Headers["X-LocIntel-Signature"]}|{requestBody}");
                return Results.Ok();
            }
        );
        await stub.StartAsync();
        try
        {
            var owner = await fixture.LoginAsync(ApiFixture.UserA);
            var created = await owner.PostAsJsonAsync(
                "/api/webhooks",
                new { url = $"{stub.Urls.First()}/hooks", events = new[] { "webhook.ping" } }
            );
            created.EnsureSuccessStatusCode();
            var hook = await created.Content.ReadFromJsonAsync<JsonElement>();
            var hookId = hook.GetProperty("id").GetGuid();
            var oldSecret = hook.GetProperty("secret").GetString()!;

            var rotated = await owner.PostAsync($"/api/webhooks/{hookId}/rotate-secret", null);
            rotated.EnsureSuccessStatusCode();
            var newSecret = (await rotated.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("secret")
                .GetString()!;

            (await owner.PostAsync($"/api/webhooks/{hookId}/ping", null)).EnsureSuccessStatusCode();
            for (var i = 0; i < 200 && received.Count == 0; i++)
                await Task.Delay(100);
            string delivery;
            lock (received)
                delivery = Assert.Single(received);

            // header carries TWO v1 entries during the window; each verifies
            // against its own secret, so a consumer mid-swap never rejects
            var (header, requestBody) = (delivery.Split('|')[0], delivery.Split('|', 2)[1]);
            var parts = header.Split(',');
            var timestamp = parts[0]["t=".Length..];
            var signatures = parts.Skip(1).Select(p => p["v1=".Length..]).ToArray();
            Assert.Equal(2, signatures.Length);
            string Expected(string secret) =>
                Convert.ToHexStringLower(
                    HMACSHA256.HashData(
                        Encoding.UTF8.GetBytes(secret),
                        Encoding.UTF8.GetBytes($"{timestamp}.{requestBody}")
                    )
                );
            Assert.Equal(Expected(newSecret), signatures[0]);
            Assert.Equal(Expected(oldSecret), signatures[1]);
        }
        finally
        {
            await stub.StopAsync();
        }
    }
}
