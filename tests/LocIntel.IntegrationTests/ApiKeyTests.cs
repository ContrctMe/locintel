using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// API keys (ADR 40): a key is a SERVICE principal holding one role - the
/// same grant model as people, so the three gates need nothing new.
/// </summary>
public class ApiKeyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private async Task<(HttpClient owner, Guid readerRoleId, string siteName)> Setup()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);

        // a limited role for keys: sites:read only
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var existing = roles
            .EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("name").GetString() == "KeyReader");
        Guid roleId;
        if (existing.ValueKind == JsonValueKind.Object)
            roleId = existing.GetProperty("id").GetGuid();
        else
        {
            var created = await owner.PostAsJsonAsync(
                "/api/roles",
                new
                {
                    name = "KeyReader",
                    grants = new[] { new { domain = "sites", action = "read" } },
                }
            );
            created.EnsureSuccessStatusCode();
            roleId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id")
                .GetGuid();
        }

        // one site to read
        var tree = await owner.GetAsync("/api/hierarchy");
        Guid rootId;
        if (tree.StatusCode == HttpStatusCode.OK)
            rootId = (await tree.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        else
        {
            var created = await owner.PostAsJsonAsync(
                "/api/hierarchy",
                new { name = "Org A", levels = new[] { "Region" } }
            );
            created.EnsureSuccessStatusCode();
            rootId = (await created.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("rootNodeId")
                .GetGuid();
        }
        var sites = await ApiFixture.GetItemsAsync(owner, "/api/sites");
        if (sites.GetArrayLength() == 0)
            (
                await owner.PostAsJsonAsync(
                    "/api/sites",
                    new
                    {
                        nodeId = rootId,
                        name = "Keyed Site",
                        timeZone = "Etc/UTC",
                    }
                )
            ).EnsureSuccessStatusCode();
        return (owner, roleId, "Keyed Site");
    }

    private HttpClient BearerClient(string secret)
    {
        var client = fixture.GuestClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        return client;
    }

    [Fact]
    public async Task Key_lifecycle_secret_once_scoped_reads_and_hard_revocation()
    {
        var (owner, roleId, _) = await Setup();

        // create: the secret leaves the server exactly once
        var created = await owner.PostAsJsonAsync(
            "/api/api-keys",
            new { name = "ci-reader", roleId }
        );
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var secret = body.GetProperty("secret").GetString()!;
        var keyId = body.GetProperty("id").GetGuid();
        Assert.StartsWith("locintel_", secret);

        // the list shows the prefix and role, never the secret or hash
        var listed = await owner.GetFromJsonAsync<JsonElement>("/api/api-keys");
        var row = listed.EnumerateArray().First(k => k.GetProperty("id").GetGuid() == keyId);
        Assert.Equal("KeyReader", row.GetProperty("role").GetString());
        Assert.Equal(secret[..16], row.GetProperty("prefix").GetString());
        Assert.False(row.TryGetProperty("secret", out _));
        Assert.False(row.TryGetProperty("secretHash", out _));

        // the key reads what its role grants...
        var service = BearerClient(secret);
        var sites = await ApiFixture.GetItemsAsync(service, "/api/sites");
        Assert.True(sites.GetArrayLength() > 0);

        // ...and nothing more (roles:manage is not in the grant)
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await service.GetAsync("/api/roles")).StatusCode
        );

        // garbage credentials are a hard 401, never a guest fall-through
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await BearerClient("locintel_not_a_real_key").GetAsync("/api/sites")).StatusCode
        );

        // revocation bites at the door
        (await owner.DeleteAsync($"/api/api-keys/{keyId}")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await service.GetAsync("/api/sites")).StatusCode
        );
    }

    [Fact]
    public async Task Suspended_orgs_take_their_keys_down_with_them()
    {
        var (owner, roleId, _) = await Setup();
        var created = await owner.PostAsJsonAsync(
            "/api/api-keys",
            new { name = "suspend-probe", roleId }
        );
        created.EnsureSuccessStatusCode();
        var secret = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("secret")
            .GetString()!;
        var service = BearerClient(secret);
        (await service.GetAsync("/api/sites")).EnsureSuccessStatusCode();

        var op = await fixture.OperatorClient();
        (
            await op.PostAsync($"/api/operator/orgs/{fixture.OrgA.Value}/suspend", null)
        ).EnsureSuccessStatusCode();
        try
        {
            // the directory learns via the outbox: poll for enforcement
            var status = HttpStatusCode.OK;
            for (var i = 0; i < 100 && status != HttpStatusCode.Forbidden; i++)
            {
                await Task.Delay(100);
                status = (await service.GetAsync("/api/sites")).StatusCode;
            }
            Assert.Equal(HttpStatusCode.Forbidden, status);
        }
        finally
        {
            (
                await op.PostAsync($"/api/operator/orgs/{fixture.OrgA.Value}/reactivate", null)
            ).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Key_custody_needs_org_manage()
    {
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await viewer.GetAsync("/api/api-keys")).StatusCode
        );
    }
}
