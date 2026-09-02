using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// AI assistance behind the local heuristic adapter (the test host never
/// talks to a vendor): suggestions return without writing anything, the
/// entitlement gates them, and the case brief redacts what the reader may
/// not see.
/// </summary>
public class AssistTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Incident_suggestion_and_case_brief_are_gated_and_read_only()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var hierarchy = await owner.GetAsync("/api/hierarchy");
        Guid rootId;
        if (hierarchy.StatusCode == HttpStatusCode.OK)
            rootId = (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
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
        var site = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = rootId,
                name = "Assist Store",
                timeZone = "Etc/UTC",
            }
        );
        site.EnsureSuccessStatusCode();
        var siteId = (await site.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var reported = await owner.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId,
                category = "Other",
                severity = "Low",
                title = "Incident at the front",
                occurredAt = DateTimeOffset.UtcNow,
                narrative = "A crew of three subjects filled bags in cosmetics and walked out past the greeter. Loss about $1,800 in fragrance.",
            }
        );
        reported.EnsureSuccessStatusCode();
        var incidentId = (await reported.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var assist = await owner.PostAsync($"/api/incidents/{incidentId}/assist", null);
        Assert.True(assist.IsSuccessStatusCode, await assist.Content.ReadAsStringAsync());
        var suggestion = await assist.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("local", suggestion.GetProperty("provider").GetString());
        Assert.Equal("OrganizedRetailCrime", suggestion.GetProperty("category").GetString());
        Assert.Equal("High", suggestion.GetProperty("severity").GetString());
        Assert.StartsWith(
            "A crew of three subjects",
            suggestion.GetProperty("summary").GetString()
        );
        Assert.True(suggestion.GetProperty("confidence").GetDouble() > 0.5);
        // read-only: the incident is untouched until the person applies it
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/incidents/{incidentId}");
        Assert.Equal("Other", detail.GetProperty("category").GetString());

        // a case brief redacts a record the reader cannot see
        var entity = await owner.PostAsJsonAsync(
            "/api/entities",
            new { kind = "Person", displayName = "Fragrance Crew Lead" }
        );
        var entityId = (await entity.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var opened = await owner.PostAsJsonAsync(
            "/api/cases",
            new { title = "Fragrance crew", incidentIds = new[] { incidentId } }
        );
        var caseId = (await opened.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await owner.PostAsJsonAsync($"/api/cases/{caseId}/entities", new { entityId })
        ).EnsureSuccessStatusCode();
        var brief = await owner.PostAsync($"/api/cases/{caseId}/assist/brief", null);
        Assert.True(brief.IsSuccessStatusCode, await brief.Content.ReadAsStringAsync());
        var text = (await brief.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("text")
            .GetString()!;
        Assert.Contains("Fragrance crew", text);
        Assert.Contains("Fragrance Crew Lead", text);

        // a member with cases:read only (no entities:read) is on the case but sees the record redacted
        var role = await owner.PostAsJsonAsync(
            "/api/roles",
            new
            {
                name = "Brief Reader",
                grants = new[] { new { domain = "cases", action = "read" } },
            }
        );
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var readerId = await fixture.CreateMemberAsync("brief-reader@locintel.local", fixture.OrgA);
        (
            await owner.PostAsJsonAsync($"/api/roles/{roleId}/assign", new { userId = readerId })
        ).EnsureSuccessStatusCode();
        (
            await owner.PostAsJsonAsync(
                $"/api/cases/{caseId}/members",
                new { userId = readerId, role = "Reviewer" }
            )
        ).EnsureSuccessStatusCode();
        var reader = await fixture.LoginAsync("brief-reader@locintel.local");
        var redacted = await reader.PostAsync($"/api/cases/{caseId}/assist/brief", null);
        Assert.True(redacted.IsSuccessStatusCode, await redacted.Content.ReadAsStringAsync());
        var redactedText = (await redacted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("text")
            .GetString()!;
        Assert.Contains("restricted record", redactedText);
        Assert.DoesNotContain("Fragrance Crew Lead", redactedText);

        // gate 1
        var op = await fixture.OperatorClient();
        (
            await op.PutAsJsonAsync(
                $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/ai.assist",
                new { value = "false" }
            )
        ).EnsureSuccessStatusCode();
        try
        {
            Assert.Equal(
                HttpStatusCode.PaymentRequired,
                (await owner.PostAsync($"/api/incidents/{incidentId}/assist", null)).StatusCode
            );
            Assert.Equal(
                HttpStatusCode.PaymentRequired,
                (await owner.PostAsync($"/api/cases/{caseId}/assist/brief", null)).StatusCode
            );
        }
        finally
        {
            (
                await op.PutAsJsonAsync(
                    $"/api/operator/orgs/{fixture.OrgA.Value}/entitlements/ai.assist",
                    new { value = "true" }
                )
            ).EnsureSuccessStatusCode();
        }
        // other tenants
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.PostAsync($"/api/incidents/{incidentId}/assist", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.PostAsync($"/api/cases/{caseId}/assist/brief", null)).StatusCode
        );
    }
}
