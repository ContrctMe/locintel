using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LocIntel.IntegrationTests;

public class UploadReadTests(UploadReadFixture fixture) : IClassFixture<UploadReadFixture>
{
    [Fact]
    public async Task Csv_bytes_are_read_without_holding_a_database_connection()
    {
        using var client = await fixture.LoginAsync(ApiFixture.UserA);
        var csv = "external_id,name,time_zone,node,status\nx,Store,UTC,missing,open";
        using var created = await client.PostAsJsonAsync(
            "/api/files",
            new
            {
                name = "sites.csv",
                contentType = "text/csv",
                sizeBytes = Encoding.UTF8.GetByteCount(csv),
            }
        );
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("fileId")
            .GetGuid();
        var store = fixture.Factory.Services.GetRequiredService<IObjectStore>();
        await store.WriteAsync(
            $"{RegionId.Default.Value}/{fixture.OrgA.Value}/files/{id}",
            new MemoryStream(Encoding.UTF8.GetBytes(csv)),
            "text/csv"
        );
        await using (var db = new NpgsqlConnection(fixture.PostgresConnectionString))
        {
            await db.OpenAsync();
            await using var command = new NpgsqlCommand(
                "UPDATE storage.files SET status = 'Clean' WHERE id = @id",
                db
            );
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync();
        }
        var reads = 0;
        fixture.Probe.BeforeRead = () =>
        {
            reads++;
            Assert.True(fixture.Probe.ConnectionsObserved > 0);
            Assert.Empty(fixture.Probe.OpenConnections);
            return Task.CompletedTask;
        };
        try
        {
            using var staged = await client.PostAsJsonAsync(
                "/api/ingest/uploads",
                new { fileId = id }
            );
            Assert.True(staged.IsSuccessStatusCode, await staged.Content.ReadAsStringAsync());
            Assert.Equal(1, reads);
        }
        finally
        {
            fixture.Probe.BeforeRead = null;
        }
    }
}
