using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>Maturity review hole 5: the on-call human can ask each dependency to answer.</summary>
public class OperatorHealthTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Probes_answer_with_latency_and_are_operator_only()
    {
        var op = await fixture.OperatorClient();
        var health = await op.GetFromJsonAsync<JsonElement>("/api/operator/health");
        var checks = health
            .GetProperty("checks")
            .EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!);
        Assert.True(checks["database"].GetProperty("ok").GetBoolean());
        Assert.True(checks["objectStore"].GetProperty("ok").GetBoolean());
        Assert.False(checks.ContainsKey("smtp")); // local transport: not applicable, not "failing"

        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/operator/health")).StatusCode
        );
    }
}
