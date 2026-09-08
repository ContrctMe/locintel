using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LocIntel.IntegrationTests;

public class ConnectionPoolTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Fleet_reads_release_the_pool_before_cross_module_scope_queries()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        var root = await ApiFixture.EnsureRootAsync(owner);
        var siteResponse = await owner.PostAsJsonAsync(
            "/api/sites",
            new
            {
                nodeId = root,
                name = "Pool test site",
                timeZone = "Etc/UTC",
            }
        );
        siteResponse.EnsureSuccessStatusCode();
        var siteId = (await siteResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var roles = await owner.GetFromJsonAsync<JsonElement>("/api/roles");
        var keyResponse = await owner.PostAsJsonAsync(
            "/api/api-keys",
            new
            {
                name = "single-pool-read",
                roleId = roles.EnumerateArray().First().GetProperty("id").GetGuid(),
            }
        );
        keyResponse.EnsureSuccessStatusCode();
        var secret = (await keyResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("secret")
            .GetString();
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:locintel",
                fixture.AppConnectionString + ";Maximum Pool Size=1;Timeout=2"
            );
            builder.UseSetting("ConnectionStrings:locintel-messaging", fixture.AppConnectionString);
        });
        using var client = host.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        foreach (
            var path in new[]
            {
                "/api/sites?limit=1",
                "/api/listings/feed?limit=1",
                "/public/sites?limit=1",
                "/api/sites/open-now",
                $"/api/sites/{siteId}",
                $"/public/sites/{siteId}",
            }
        )
        {
            var response = await client.GetAsync(path);
            Assert.True(
                response.IsSuccessStatusCode,
                $"{path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}"
            );
        }
    }

    [Theory]
    [InlineData("", 20)]
    [InlineData(";Maximum Pool Size=7", 7)]
    [InlineData(";Minimum Pool Size=25", 25)]
    public async Task Runtime_bounds_default_pools_and_preserves_explicit_budgets(
        string suffix,
        int expected
    )
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:locintel", fixture.AppConnectionString + suffix)
        );
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var regions = host.Services.GetRequiredService<IRegionDataSources>();
        Assert.Equal(
            expected,
            new NpgsqlConnectionStringBuilder(
                configuration.GetConnectionString("locintel")
            ).MaxPoolSize
        );
        Assert.Equal(
            expected,
            new NpgsqlConnectionStringBuilder(
                regions.For(RegionId.Default).ConnectionString
            ).MaxPoolSize
        );
    }
}
