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
