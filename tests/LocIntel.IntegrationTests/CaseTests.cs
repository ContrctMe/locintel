using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Investigations: membership-or-scope visibility, members work while
/// managers run, the custody chain records every touch of evidence, legal
/// hold cascades to Storage over the outbox, and the package export is
/// itself a custody event.
/// </summary>
public class CaseTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Case_round_trip_with_custody_chain_and_cascading_hold()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var rootId = await RootAsync(owner);
        var siteId = await SiteAsync(owner, rootId, "Case Store");
        var incidentId = await IncidentAsync(owner, siteId, "Register sweep");
        var entityId = await EntityAsync(owner, "Sweeper");
        var fileId = await UploadAsync(owner, "receipt.jpg");

        var opened = await owner.PostAsJsonAsync(
            "/api/cases",
            new
            {
                title = "ORC crew, spring",
                summary = "Three stores, same MO.",
                priority = "High",
                incidentIds = new[] { incidentId },
            }
        );
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
        var caseId = (await opened.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.Equal("Open", detail.GetProperty("status").GetString());
        Assert.Equal(ApiFixture.UserA, detail.GetProperty("lead").GetString());
        Assert.Single(detail.GetProperty("members").EnumerateArray());
        var incident = Assert.Single(detail.GetProperty("incidents").EnumerateArray());
        Assert.Equal("Register sweep", incident.GetProperty("title").GetString());
        Assert.True(detail.GetProperty("canManage").GetBoolean());

        // entity on the case resolves through the need-to-know directory
        (
            await owner.PostAsJsonAsync(
                $"/api/cases/{caseId}/entities",
                new { entityId, note = "seen on video" }
            )
        ).EnsureSuccessStatusCode();
        // a task, a note, evidence
        var task = await owner.PostAsJsonAsync(
            $"/api/cases/{caseId}/tasks",
            new { title = "Pull CCTV" }
        );
        task.EnsureSuccessStatusCode();
        var taskId = (await task.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        (
            await owner.PostAsJsonAsync($"/api/cases/{caseId}/notes", new { body = "Called PD." })
        ).EnsureSuccessStatusCode();
        var evidence = await owner.PostAsJsonAsync(
            $"/api/cases/{caseId}/evidence",
            new { fileId, label = "Receipt from register 3" }
        );
        Assert.True(evidence.IsSuccessStatusCode, await evidence.Content.ReadAsStringAsync());
        var evidenceId = (await evidence.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await owner.PostAsJsonAsync($"/api/cases/{caseId}/evidence", new { fileId })
            ).StatusCode
        );

        detail = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        var entity = Assert.Single(detail.GetProperty("entities").EnumerateArray());
        Assert.Equal("Sweeper", entity.GetProperty("displayName").GetString());
        Assert.False(entity.GetProperty("restricted").GetBoolean());
        Assert.Single(detail.GetProperty("tasks").EnumerateArray());
        Assert.Single(detail.GetProperty("notes").EnumerateArray());
        var item = Assert.Single(detail.GetProperty("evidence").EnumerateArray());
        Assert.Equal("receipt.jpg", item.GetProperty("fileName").GetString());

        // download through the case: signed URL + custody event
        var download = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/cases/{caseId}/evidence/{evidenceId}/download"
        );
        Assert.Equal("receipt.jpg", download.GetProperty("fileName").GetString());
        Assert.False(string.IsNullOrEmpty(download.GetProperty("url").GetString()));

        // hold cascades to the file over the outbox
        (
            await owner.PostAsJsonAsync($"/api/cases/{caseId}/hold", new { hold = true })
        ).EnsureSuccessStatusCode();
        var held = false;
        for (var i = 0; i < 50 && !held; i++)
        {
            await Task.Delay(100);
            var file = await owner.GetFromJsonAsync<JsonElement>($"/api/files?q=receipt");
            held = file.GetProperty("items")
                .EnumerateArray()
                .Any(f =>
                    f.GetProperty("id").GetGuid() == fileId
                    && f.GetProperty("legalHold").GetBoolean()
                );
        }
        Assert.True(held, "the evidence file should be under legal hold after the case is held");
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/cases/{caseId}/evidence/{evidenceId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await owner.DeleteAsync($"/api/cases/{caseId}")).StatusCode
        );

        // the package export logs custody on every file and carries the chain
        var package = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}/package");
        Assert.Equal(
            "ORC crew, spring",
            package.GetProperty("case").GetProperty("title").GetString()
        );
        var actions = package
            .GetProperty("custody")
            .EnumerateArray()
            .Select(c => c.GetProperty("action").GetString())
            .ToList();
        Assert.Equal(["Added", "Downloaded", "HoldPlaced", "Exported"], actions);

        // release, tick the task, close with a disposition
        (
            await owner.PostAsJsonAsync($"/api/cases/{caseId}/hold", new { hold = false })
        ).EnsureSuccessStatusCode();
        (
            await owner.PostAsJsonAsync(
                $"/api/cases/{caseId}/tasks/{taskId}/done",
                new { done = true }
            )
        ).EnsureSuccessStatusCode();
        (
            await owner.PostAsJsonAsync(
                $"/api/cases/{caseId}/close",
                new { disposition = "ReferredToPolice", note = "Report 26-1234" }
            )
        ).EnsureSuccessStatusCode();
        detail = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.Equal("Closed", detail.GetProperty("status").GetString());
        Assert.Equal("ReferredToPolice", detail.GetProperty("disposition").GetString());
        Assert.NotEqual(
            JsonValueKind.Null,
            detail.GetProperty("tasks")[0].GetProperty("doneAt").ValueKind
        );
        var custody = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}/custody");
        Assert.Equal(5, custody.GetProperty("events").GetArrayLength());

        // trash and restore (tier 2)
        (await owner.DeleteAsync($"/api/cases/{caseId}")).EnsureSuccessStatusCode();
        var live = await owner.GetFromJsonAsync<JsonElement>("/api/cases");
        Assert.DoesNotContain(
            live.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == caseId
        );
        (await owner.PostAsync($"/api/cases/{caseId}/restore", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/cases/{caseId}")).StatusCode);
    }

    [Fact]
    public async Task Visibility_is_membership_or_scope_and_members_work_but_do_not_manage()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var rootId = await RootAsync(owner);
        var siteId = await SiteAsync(owner, rootId, "Members Store");
        var incidentId = await IncidentAsync(owner, siteId, "Members incident");

        // a reader with cases:read, no scope-relevant incident, not a member
        var role = await owner.PostAsJsonAsync(
            "/api/roles",
            new
            {
                name = "Case Reader",
                grants = new[] { new { domain = "cases", action = "read" } },
            }
        );
        role.EnsureSuccessStatusCode();
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var readerId = await fixture.CreateMemberAsync("case-reader@locintel.local", fixture.OrgA);
        // assigned to a subtree that holds NO sites, so scope never reveals the case
        var empty = await owner.PostAsJsonAsync(
            "/api/hierarchy/nodes",
            new { parentId = rootId, name = "Empty Region" }
        );
        empty.EnsureSuccessStatusCode();
        var emptyPath = (await empty.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("path")
            .GetString();
        (
            await owner.PostAsJsonAsync(
                $"/api/roles/{roleId}/assign",
                new { userId = readerId, scopePath = emptyPath }
            )
        ).EnsureSuccessStatusCode();
        var reader = await fixture.LoginAsync("case-reader@locintel.local");

        var opened = await owner.PostAsJsonAsync(
            "/api/cases",
            new { title = "Members case", incidentIds = new[] { incidentId } }
        );
        opened.EnsureSuccessStatusCode();
        var caseId = (await opened.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await reader.GetAsync($"/api/cases/{caseId}")).StatusCode
        );

        // membership opens it; the member can work but not manage
        (
            await owner.PostAsJsonAsync(
                $"/api/cases/{caseId}/members",
                new { userId = readerId, role = "Investigator" }
            )
        ).EnsureSuccessStatusCode();
        var detail = await reader.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.True(detail.GetProperty("canWork").GetBoolean());
        Assert.False(detail.GetProperty("canManage").GetBoolean());
        (
            await reader.PostAsJsonAsync($"/api/cases/{caseId}/notes", new { body = "On it." })
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await reader.PostAsJsonAsync(
                    $"/api/cases/{caseId}/close",
                    new { disposition = "Resolved" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await reader.PostAsJsonAsync(
                    $"/api/cases/{caseId}/members",
                    new { userId = readerId }
                )
            ).StatusCode
        );
        var mine = await reader.GetFromJsonAsync<JsonElement>("/api/cases?mine=true");
        Assert.Contains(
            mine.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == caseId
        );

        // other tenants and tiers
        var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/cases/{caseId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await outsider.PostAsJsonAsync($"/api/cases/{caseId}/incidents", new { incidentId })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await fixture.GuestClient().GetAsync("/api/cases")).StatusCode
        );
        var viewer = await fixture.LoginAsync(ApiFixture.ViewerA);
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/cases")).StatusCode);
    }

    private static async Task<Guid> RootAsync(HttpClient client)
    {
        var hierarchy = await client.GetAsync("/api/hierarchy");
        if (hierarchy.StatusCode == HttpStatusCode.OK)
            return (await hierarchy.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("nodes")
                .EnumerateArray()
                .First(n => n.GetProperty("depth").GetInt32() == 0)
                .GetProperty("id")
                .GetGuid();
        var created = await client.PostAsJsonAsync(
            "/api/hierarchy",
            new { name = "Org A", levels = new[] { "Region" } }
        );
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("rootNodeId")
            .GetGuid();
    }

    private static async Task<Guid> SiteAsync(HttpClient client, Guid nodeId, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId,
                name,
                timeZone = "Etc/UTC",
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task<Guid> IncidentAsync(HttpClient client, Guid siteId, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                siteId,
                category = "OrganizedRetailCrime",
                severity = "High",
                title,
                occurredAt = DateTimeOffset.UtcNow,
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task<Guid> EntityAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/entities",
            new { kind = "Person", displayName = name }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string name)
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7 };
        var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name,
                contentType = "image/jpeg",
                sizeBytes = bytes.Length,
            }
        );
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var fileId = body.GetProperty("fileId").GetGuid();
        var put = new HttpRequestMessage(
            HttpMethod.Put,
            body.GetProperty("ticket").GetProperty("url").GetString()
        )
        {
            Content = new ByteArrayContent(bytes),
        };
        (await client.SendAsync(put)).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/files/{fileId}/complete", null)).EnsureSuccessStatusCode();
        return fileId;
    }
}
