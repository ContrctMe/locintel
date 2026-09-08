using System.Text.Json;

namespace LocIntel.IntegrationTests;

/// <summary>
/// ADR 16: the spec is a REVIEWED ARTIFACT. This test snapshots the running
/// app's OpenAPI document into the frontend workspace; an uncommitted diff
/// after a test run means the contract changed - review it like code. The TS
/// client and query hooks generate from the committed file.
/// </summary>
public class OpenApiSnapshotTest(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Snapshot_spec_into_workspace()
    {
        var spec = await fixture.GuestClient().GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/sites", spec);
        Assert.Contains("/api/ingest/uploads", spec);
        using var document = JsonDocument.Parse(spec);
        var schema = document
            .RootElement.GetProperty("paths")
            .GetProperty("/api/patrols/routes/{id}/schedules")
            .GetProperty("post")
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()!;
        var properties = document
            .RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schema.Split('/')[^1])
            .GetProperty("properties");
        Assert.True(properties.TryGetProperty("startLocal", out _));
        Assert.False(properties.TryGetProperty("opens", out _));

        var dir = FindRepoRoot();
        if (dir is not null) // repo layout present (skip in detached CI sandboxes)
        {
            var target = Path.Combine(dir, "web", "packages", "api", "openapi.json");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, spec);
        }
    }

    private static string? FindRepoRoot()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
            if (File.Exists(Path.Combine(dir.FullName, "LocIntel.slnx")))
                return dir.FullName;
        return null;
    }
}
