using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The typed-response RATCHET (maturity review design debt): an endpoint
/// whose OpenAPI schema is the IResult stub generates a client that looks
/// safe while accepting anything. Every such operation is pinned below; a
/// NEW one fails this test (declare the response type - a named record plus
/// [ProducesResponseType], see SiteAttributeEndpoints), and converting an
/// old one fails it too until its line is DELETED here. The list only
/// shrinks.
/// </summary>
public class TypedResponseRatchetTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly string[] Grandfathered = [];

    [Fact]
    public async Task Untyped_operations_only_ever_shrink()
    {
        var spec = await fixture.GuestClient().GetStringAsync("/openapi/v1.json");
        using var doc = JsonDocument.Parse(spec);
        var untyped = new HashSet<string>();
        foreach (var path in doc.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var op in path.Value.EnumerateObject())
        {
            if (!op.Value.TryGetProperty("responses", out var responses))
                continue;
            var successes = responses
                .EnumerateObject()
                .Where(response =>
                    int.TryParse(response.Name, out var status) && status is >= 200 and < 300
                )
                .ToList();
            if (
                successes.Count > 0
                && successes.All(response =>
                    response.Value.TryGetProperty("content", out var content)
                    && content.TryGetProperty("application/json", out var json)
                    && json.TryGetProperty("schema", out var schema)
                    && schema.TryGetProperty("$ref", out var reference)
                    && reference.GetString()!.EndsWith("/IResult")
                )
            )
                untyped.Add($"{op.Name.ToUpperInvariant()} {path.Name}");
        }

        var newcomers = untyped.Except(Grandfathered).Order().ToArray();
        Assert.True(
            newcomers.Length == 0,
            "new UNTYPED endpoints (declare a response type instead of grandfathering): "
                + string.Join(", ", newcomers)
        );
        var converted = Grandfathered.Except(untyped).Order().ToArray();
        Assert.True(
            converted.Length == 0,
            "these are typed now - delete their lines from Grandfathered so the ratchet holds: "
                + string.Join(", ", converted)
        );
    }
}
