using System.Net;
using System.Text;
using System.Text.Json;
using LocIntel.Modules.Ingest;
using LocIntel.Modules.Spatial.Overlays;
using LocIntel.Modules.Storage;
using LocIntel.Platform.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace LocIntel.IntegrationTests;

public sealed class DataPlaneBoundaryTests
{
    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://[::ffff:127.0.0.1]/")]
    [InlineData("https://10.0.0.1/")]
    [InlineData("https://100.64.0.1/")]
    [InlineData("https://[fc00::1]/")]
    [InlineData("https://example.com/#fragment")]
    public void Production_destinations_reject_private_or_ambiguous_urls(string url) =>
        Assert.False(PublicHttp.IsAllowedUrl(url));

    [Fact]
    public async Task Outbound_connections_pin_public_addresses_and_do_not_forward_redirects()
    {
        Assert.True(PublicHttp.IsAllowedUrl("https://example.com/hook"));
        using (var http = new HttpClient(PublicHttp.CreateHandler()))
        {
            var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
                http.GetAsync("https://127.0.0.1/")
            );
            Assert.Contains("prohibited", failure.ToString());
        }
        var reached = false;
        var app = WebApplication.CreateSlimBuilder().Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapGet("/redirect", () => Results.Redirect("/target"));
        app.MapGet(
            "/target",
            () =>
            {
                reached = true;
                return Results.Ok();
            }
        );
        await app.StartAsync();
        try
        {
            using var handler = PublicHttp.CreateHandler(allowLoopback: true);
            Assert.False(handler.UseCookies);
            Assert.False(handler.UseProxy);
            using var http = new HttpClient(handler);
            http.DefaultRequestHeaders.Add("X-Api-Key", "test-only-key");
            using var response = await http.GetAsync(app.Urls.Single() + "/redirect");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.False(reached);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Content_and_parser_limits_fail_before_expensive_work()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedRead.ReadAsync(new MemoryStream(new byte[9]), 8, CancellationToken.None)
        );
        Assert.Throws<InvalidDataException>(() => CsvParser.Parse("name,name\nx,y"));
        Assert.Throws<InvalidDataException>(() =>
            CsvParser.Parse("name\n" + new string('x', IngestLimits.MaxFieldChars + 1))
        );
        Assert.Throws<InvalidDataException>(() =>
            CsvParser.Parse(
                "name\n" + string.Concat(Enumerable.Repeat("x\n", IngestLimits.MaxRows + 1))
            )
        );
        Assert.Throws<InvalidDataException>(() =>
            CsvParser.Parse(string.Join(',', Enumerable.Range(0, IngestLimits.MaxColumns + 1)))
        );
        var polygon = JsonSerializer.SerializeToElement(
            new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    Enumerable.Repeat(new[] { 0d, 0d }, GeoJsonFeatures.MaxVertices + 1).ToArray(),
                },
            }
        );
        Assert.Contains("limits", GeoJsonFeatures.Parse(polygon).Error);
        var excessive = JsonSerializer.SerializeToElement(
            new
            {
                type = "FeatureCollection",
                features = new object[GeoJsonFeatures.MaxFeatures + 1],
            }
        );
        Assert.Contains("limits", GeoJsonFeatures.Parse(excessive).Error);
        Assert.NotNull(
            GeoJsonFeatures.Parse(JsonSerializer.SerializeToElement(new { type = 1 })).Error
        );
    }

    [Fact]
    public async Task Local_tickets_bind_operation_and_publish_once_with_bounded_bytes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "locintel-ticket-test-" + Guid.NewGuid().ToString("N")
        );
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Storage:LocalRoot"] = root,
                    ["Secrets:LocalMasterKey"] = Convert.ToBase64String(new byte[32]),
                }
            )
            .Build();
        var store = new LocalObjectStore(configuration);
        try
        {
            var ticket = await store.CreateUploadTicketAsync("org/file", "text/plain", 4);
            var token = ticket.Url.Split('/').Last();
            Assert.NotNull(store.Redeem(token, "upload"));
            Assert.Null(store.Redeem(token, "download"));
            var read = await store.GetDownloadUrlAsync("org/file", TimeSpan.FromMinutes(1));
            Assert.Null(store.Redeem(read.ToString().Split('/').Last(), "upload"));
            await store.UploadAsync(
                "org/file",
                new MemoryStream("safe"u8.ToArray()),
                4,
                CancellationToken.None
            );
            await Assert.ThrowsAsync<IOException>(() =>
                store.UploadAsync(
                    "org/file",
                    new MemoryStream("evil"u8.ToArray()),
                    4,
                    CancellationToken.None
                )
            );
            await store.DeleteAsync("org/file");
            await Assert.ThrowsAsync<IOException>(() =>
                store.UploadAsync(
                    "org/file",
                    new MemoryStream("evil"u8.ToArray()),
                    4,
                    CancellationToken.None
                )
            );
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.UploadAsync(
                    "org/oversize",
                    new MemoryStream(new byte[5]),
                    4,
                    CancellationToken.None
                )
            );
            Assert.Null(await store.GetLengthAsync("org/oversize"));
            Assert.Throws<InvalidOperationException>(() =>
                store.PathFor("../" + Path.GetFileName(root) + "-sibling/file")
            );
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
