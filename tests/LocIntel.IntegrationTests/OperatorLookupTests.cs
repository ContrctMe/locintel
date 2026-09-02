using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>Maturity review hole 2: support starts from the ticket's From line.</summary>
public class OperatorLookupTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Email_search_finds_the_person_and_their_orgs()
    {
        var op = await fixture.OperatorClient();
        var hits = await op.GetFromJsonAsync<JsonElement>("/api/operator/users?q=user-ab");
        var hit = hits.EnumerateArray()
            .Single(u => u.GetProperty("email").GetString() == ApiFixture.UserBoth);
        var orgs = hit.GetProperty("orgs")
            .EnumerateArray()
            .Select(o => o.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("Org A", orgs);
        Assert.Contains("Org B", orgs);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await op.GetAsync("/api/operator/users?q=x")).StatusCode
        );
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await owner.GetAsync("/api/operator/users?q=user")).StatusCode
        );
    }
}
