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

        var downloadPath = $"/api/cases/{caseId}/evidence/{evidenceId}/download";
        Assert.Equal(
            HttpStatusCode.MethodNotAllowed,
            (await owner.GetAsync(downloadPath)).StatusCode
        );
        using var forged = new HttpRequestMessage(HttpMethod.Post, downloadPath);
        forged.Headers.Add("Origin", "https://evil.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.SendAsync(forged)).StatusCode);
        using var otherOrg = await fixture.LoginAsync(ApiFixture.UserB);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherOrg.PostAsync(downloadPath, null)).StatusCode
        );
        var issuanceKey = Guid.NewGuid().ToString();
        HttpRequestMessage Issue() =>
            new(HttpMethod.Post, downloadPath) { Headers = { { "Idempotency-Key", issuanceKey } } };
        using var issued = await owner.SendAsync(Issue());
        issued.EnsureSuccessStatusCode();
        var download = await issued.Content.ReadFromJsonAsync<JsonElement>();
        using var replayed = await owner.SendAsync(Issue());
        // Signed evidence URLs are not stored for replay. A duplicate is suppressed
        // without issuing a second access event or redisclosing a bearer URL.
        Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
        var custodyAfter = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/cases/{caseId}/custody"
        );
        Assert.Single(
            custodyAfter.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("action").GetString() == "DownloadAccessIssued"
        );
        Assert.DoesNotContain(
            custodyAfter.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("action").GetString() == "Downloaded"
        );
        Assert.Equal("receipt.jpg", download.GetProperty("fileName").GetString());
        Assert.False(string.IsNullOrEmpty(download.GetProperty("url").GetString()));

        // hold cascades to the file over the outbox
        (
            await owner.PostAsJsonAsync($"/api/cases/{caseId}/hold", new { hold = true })
        ).EnsureSuccessStatusCode();
        var held = false;
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                var file = await owner.GetFromJsonAsync<JsonElement>($"/api/files?q=receipt");
                held = file.GetProperty("items")
                    .EnumerateArray()
                    .Any(f =>
                        f.GetProperty("id").GetGuid() == fileId
                        && f.GetProperty("legalHold").GetBoolean()
                    );
                return !(!held);
            },
            "held"
        );
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
        Assert.Equal(["Added", "DownloadAccessIssued", "HoldPlaced", "Exported"], actions);

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
            HttpStatusCode.Forbidden,
            (
                await reader.PostAsJsonAsync(
                    $"/api/cases/{caseId}/close",
                    new { disposition = "Resolved" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
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

        var fileId = await UploadAsync(owner, "revoked-evidence.jpg");
        var added = await reader.PostAsJsonAsync($"/api/cases/{caseId}/evidence", new { fileId });
        added.EnsureSuccessStatusCode();
        var evidenceId = (await added.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var downloadPath = $"/api/cases/{caseId}/evidence/{evidenceId}/download";
        (await reader.PostAsync(downloadPath, null)).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.GetAsync($"/api/cases/{caseId}/package")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.DeleteAsync($"/api/cases/{caseId}/evidence/{evidenceId}")).StatusCode
        );

        var memberId = detail
            .GetProperty("members")
            .EnumerateArray()
            .Single(m => m.GetProperty("userId").GetGuid() == readerId)
            .GetProperty("id")
            .GetGuid();
        (
            await owner.DeleteAsync($"/api/cases/{caseId}/members/{memberId}")
        ).EnsureSuccessStatusCode();
        // The already-authenticated session loses access on its next request.
        foreach (var suffix in new[] { "", "/custody", "/package" })
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await reader.GetAsync($"/api/cases/{caseId}{suffix}")).StatusCode
            );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await reader.PostAsync(downloadPath, null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await reader.PostAsync($"/api/cases/{caseId}/assist/brief", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await reader.PostAsJsonAsync($"/api/cases/{caseId}/notes", new { body = "Revoked" })
            ).StatusCode
        );
        mine = await reader.GetFromJsonAsync<JsonElement>("/api/cases?mine=true");
        Assert.DoesNotContain(
            mine.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == caseId
        );
        var custody = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}/custody");
        Assert.Equal(
            new[] { "Added", "DownloadAccessIssued" },
            custody
                .GetProperty("events")
                .EnumerateArray()
                .Select(e => e.GetProperty("action").GetString())
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
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/cases")).StatusCode);
    }

    [Fact]
    public async Task Scoped_readers_see_custody_but_cannot_issue_evidence_access_or_work_the_case()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var siteId = await SiteAsync(owner, await RootAsync(owner), "Scoped case store");
        var incidentId = await IncidentAsync(owner, siteId, "Scoped case incident");
        var caseId = await OpenAsync(owner, "Scope-only case", incidentId);
        var fileId = await UploadAsync(owner, "scope-evidence.jpg");
        var added = await owner.PostAsJsonAsync($"/api/cases/{caseId}/evidence", new { fileId });
        added.EnsureSuccessStatusCode();
        var evidenceId = (await added.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var role = await owner.PostAsJsonAsync(
            "/api/roles",
            new
            {
                name = "Scoped case reader",
                grants = new[] { new { domain = "cases", action = "read" } },
            }
        );
        role.EnsureSuccessStatusCode();
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var readerId = await fixture.CreateMemberAsync(
            "scope-case-reader@locintel.local",
            fixture.OrgA
        );
        (
            await owner.PostAsJsonAsync($"/api/roles/{roleId}/assign", new { userId = readerId })
        ).EnsureSuccessStatusCode();
        using var reader = await fixture.LoginAsync("scope-case-reader@locintel.local");

        var detail = await reader.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.False(detail.GetProperty("canWork").GetBoolean());
        Assert.False(detail.GetProperty("canManage").GetBoolean());
        Assert.Equal(
            "scope-evidence.jpg",
            Assert
                .Single(detail.GetProperty("evidence").EnumerateArray())
                .GetProperty("fileName")
                .GetString()
        );
        var visible = await reader.GetFromJsonAsync<JsonElement>("/api/cases");
        Assert.Contains(
            visible.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == caseId
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await reader.PostAsync($"/api/cases/{caseId}/evidence/{evidenceId}/download", null)
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await reader.PostAsJsonAsync($"/api/cases/{caseId}/evidence", new { fileId })
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await reader.PostAsJsonAsync(
                    $"/api/cases/{caseId}/notes",
                    new { body = "Not a member" }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.GetAsync($"/api/cases/{caseId}/package")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await reader.PostAsync($"/api/cases/{caseId}/assist/brief", null)).StatusCode
        );
        var custody = await reader.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}/custody");
        Assert.Equal(
            "Added",
            Assert
                .Single(custody.GetProperty("events").EnumerateArray())
                .GetProperty("action")
                .GetString()
        );

        var linkId = Assert
            .Single(detail.GetProperty("incidents").EnumerateArray())
            .GetProperty("id")
            .GetGuid();
        (
            await owner.DeleteAsync($"/api/cases/{caseId}/incidents/{linkId}")
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await reader.GetAsync($"/api/cases/{caseId}")).StatusCode
        );
        visible = await reader.GetFromJsonAsync<JsonElement>("/api/cases");
        Assert.DoesNotContain(
            visible.GetProperty("items").EnumerateArray(),
            c => c.GetProperty("id").GetGuid() == caseId
        );
    }

    [Fact]
    public async Task Evidence_rejects_foreign_files_and_wrong_case_ids_without_recording_custody()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        using var outsider = await fixture.LoginAsync(ApiFixture.UserB);
        var caseId = await OpenAsync(owner, "Evidence isolation");
        var otherCaseId = await OpenAsync(owner, "Another case");
        var foreignFile = await UploadAsync(outsider, "foreign-evidence.jpg");
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await owner.PostAsJsonAsync(
                    $"/api/cases/{caseId}/evidence",
                    new { fileId = foreignFile }
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await owner.PostAsJsonAsync(
                    $"/api/cases/{caseId}/evidence",
                    new { fileId = Guid.NewGuid() }
                )
            ).StatusCode
        );

        // A file awaiting upload may be linked, but must never yield a download URL.
        var created = await owner.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "pending-evidence.jpg",
                contentType = "image/jpeg",
                sizeBytes = 7,
            }
        );
        created.EnsureSuccessStatusCode();
        var fileId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fileId")
            .GetGuid();
        var added = await owner.PostAsJsonAsync($"/api/cases/{caseId}/evidence", new { fileId });
        added.EnsureSuccessStatusCode();
        var evidenceId = (await added.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await owner.PostAsync($"/api/cases/{caseId}/evidence/{evidenceId}/download", null)
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await owner.PostAsync(
                    $"/api/cases/{otherCaseId}/evidence/{evidenceId}/download",
                    null
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.DeleteAsync($"/api/cases/{otherCaseId}/evidence/{evidenceId}")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsider.DeleteAsync($"/api/cases/{caseId}/evidence/{evidenceId}")).StatusCode
        );
        // Use a downloadable file as well: a wrong-case rejection must come
        // from parent authorization, not accidentally from the scan-state guard.
        var cleanFileId = await UploadAsync(owner, "other-case-clean.jpg");
        var cleanAdded = await owner.PostAsJsonAsync(
            $"/api/cases/{otherCaseId}/evidence",
            new { fileId = cleanFileId }
        );
        cleanAdded.EnsureSuccessStatusCode();
        var cleanEvidenceId = (await cleanAdded.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (
                await owner.PostAsync(
                    $"/api/cases/{caseId}/evidence/{cleanEvidenceId}/download",
                    null
                )
            ).StatusCode
        );
        var custody = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}/custody");
        Assert.Equal(
            "Added",
            Assert
                .Single(custody.GetProperty("events").EnumerateArray())
                .GetProperty("action")
                .GetString()
        );
        var otherCustody = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/cases/{otherCaseId}/custody"
        );
        Assert.Equal(
            "Added",
            Assert
                .Single(otherCustody.GetProperty("events").EnumerateArray())
                .GetProperty("action")
                .GetString()
        );

        (
            await owner.DeleteAsync($"/api/cases/{caseId}/evidence/{evidenceId}")
        ).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.DeleteAsync($"/api/cases/{caseId}/evidence/{evidenceId}")).StatusCode
        );
        var detail = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.Empty(detail.GetProperty("evidence").EnumerateArray());
        custody = await owner.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}/custody");
        Assert.Equal(
            new[] { "Added", "Removed" },
            custody
                .GetProperty("events")
                .EnumerateArray()
                .Select(e => e.GetProperty("action").GetString())
        );
    }

    private static async Task<Guid> OpenAsync(
        HttpClient client,
        string title,
        params Guid[] incidentIds
    )
    {
        var opened = await client.PostAsJsonAsync("/api/cases", new { title, incidentIds });
        opened.EnsureSuccessStatusCode();
        return (await opened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                var files = await client.GetFromJsonAsync<JsonElement>(
                    $"/api/files?q={Uri.EscapeDataString(name)}"
                );
                return files
                    .GetProperty("items")
                    .EnumerateArray()
                    .Any(f =>
                        f.GetProperty("id").GetGuid() == fileId
                        && f.GetProperty("status").GetString() == "Clean"
                    );
            },
            "case evidence to finish scanning"
        );
        return fileId;
    }
}
