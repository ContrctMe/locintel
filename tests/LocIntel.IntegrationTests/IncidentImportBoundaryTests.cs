using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LocIntel.IntegrationTests;

public class IncidentImportBoundaryTests(IncidentImportFixture fixture)
    : IClassFixture<IncidentImportFixture>
{
    [Theory]
    [InlineData("site,SITE\nx,y")]
    [InlineData("site,title\nx,\"unfinished")]
    public async Task Malformed_csv_is_a_client_error_and_creates_no_batch(string csv)
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(owner, Encoding.UTF8.GetBytes(csv));
        await AssertRejected(owner, id);
    }

    [Fact]
    public async Task Oversized_file_and_row_count_are_rejected_before_staging()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var large = await Prepare(owner, new byte[4 * 1024 * 1024 + 1]);
        await AssertRejected(owner, large);
        var rows = await Prepare(
            owner,
            Encoding.UTF8.GetBytes("site\n" + string.Join('\n', Enumerable.Repeat("x", 5001)))
        );
        await AssertRejected(owner, rows);
    }

    [Fact]
    public async Task Import_releases_database_connections_during_storage_read_and_rejects_undefined_enums()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var id = await Prepare(
            owner,
            Encoding.UTF8.GetBytes(
                "site,category,severity,title,occurred_at\nmissing,999,999,History,2026-09-01T00:00:00Z"
            )
        );
        var reads = 0;
        fixture.Probe.BeforeRead = () =>
        {
            reads++;
            Assert.Empty(fixture.Probe.OpenConnections);
            return Task.CompletedTask;
        };
        try
        {
            using var staged = await owner.PostAsJsonAsync(
                "/api/incidents/imports",
                new { fileId = id }
            );
            Assert.True(staged.IsSuccessStatusCode, await staged.Content.ReadAsStringAsync());
            Assert.Equal(1, reads);
            var batch = await staged.Content.ReadFromJsonAsync<JsonElement>();
            var detail = await owner.GetFromJsonAsync<JsonElement>(
                $"/api/incidents/imports/{batch.GetProperty("id").GetGuid()}"
            );
            var errors = Assert
                .Single(detail.GetProperty("rows").EnumerateArray())
                .GetProperty("errors")
                .EnumerateArray()
                .Select(e => e.GetString());
            Assert.Contains("unknown category", errors);
            Assert.Contains("unknown severity", errors);
        }
        finally
        {
            fixture.Probe.BeforeRead = null;
        }
    }

    [Fact]
    public async Task Database_field_limits_become_invalid_preview_rows()
    {
        using var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var csv =
            "site,category,severity,title,occurred_at,police_report,loss_amount\n"
            + new string('x', 201)
            + ",Theft,Low,History,2026-09-01T00:00:00Z,,\n"
            + "missing,Theft,Low,History,2026-09-01T00:00:00Z,"
            + new string('x', 101)
            + ",\n"
            + "missing,Theft,Low,History,2026-09-01T00:00:00Z,,1000000000000";
        var id = await Prepare(owner, Encoding.UTF8.GetBytes(csv));
        using var staged = await owner.PostAsJsonAsync(
            "/api/incidents/imports",
            new { fileId = id }
        );
        Assert.True(staged.IsSuccessStatusCode, await staged.Content.ReadAsStringAsync());
        var batch = await staged.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, batch.GetProperty("invalid").GetInt32());
        var detail = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/incidents/imports/{batch.GetProperty("id").GetGuid()}"
        );
        var rows = detail.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Contains(
            rows[0].GetProperty("errors").EnumerateArray(),
            error => error.GetString() == "site is limited to 200 characters"
        );
        Assert.Contains(
            rows[1].GetProperty("errors").EnumerateArray(),
            error => error.GetString() == "police_report is limited to 100 characters"
        );
        Assert.Contains(
            rows[2].GetProperty("errors").EnumerateArray(),
            error => error.GetString() == "loss_amount must be between 0 and 999999999999.99"
        );
    }

    private static async Task AssertRejected(HttpClient owner, Guid id)
    {
        using var staged = await owner.PostAsJsonAsync(
            "/api/incidents/imports",
            new { fileId = id }
        );
        Assert.Equal(HttpStatusCode.BadRequest, staged.StatusCode);
        var batches = await owner.GetFromJsonAsync<JsonElement>("/api/incidents/imports");
        Assert.DoesNotContain(
            batches.GetProperty("items").EnumerateArray(),
            b => b.GetProperty("fileId").GetGuid() == id
        );
    }

    private async Task<Guid> Prepare(HttpClient client, byte[] bytes)
    {
        using var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "import-boundary.csv",
                contentType = "text/csv",
                sizeBytes = bytes.Length,
            }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fileId")
            .GetGuid();
        var store = fixture.Factory.Services.GetRequiredService<IObjectStore>();
        await store.WriteAsync(
            $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}",
            new MemoryStream(bytes),
            "text/csv"
        );
        // Arrange the clean-file precondition; scanner/ticket behavior has its own real-flow suite.
        await using var db = new NpgsqlConnection(fixture.PostgresConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE storage.files SET status = 'Clean' WHERE id = @id",
            db
        );
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
        return id;
    }
}
