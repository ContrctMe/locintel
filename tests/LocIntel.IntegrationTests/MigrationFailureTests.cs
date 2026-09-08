using LocIntel.Api;
using LocIntel.Modules.Tenancy.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LocIntel.IntegrationTests;

public class MigrationFailureTests
{
    [Theory]
    [InlineData("42501")]
    [InlineData("42601")]
    [InlineData("28P01")]
    public async Task Permanent_database_errors_fail_without_retry(string sqlState)
    {
        var attempts = 0;
        await using var services = new ServiceCollection()
            .AddScoped<TenancyDbContext>(_ =>
            {
                attempts++;
                throw new PostgresException("permanent failure", "ERROR", "ERROR", sqlState);
            })
            .BuildServiceProvider();
        using var runner = Runner(services);
        await runner.StartAsync(default);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            runner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))
        );
        Assert.Equal(sqlState, failure.SqlState);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Startup_unavailability_retries_but_a_later_permanent_error_does_not()
    {
        var attempts = 0;
        await using var services = new ServiceCollection()
            .AddScoped<TenancyDbContext>(_ =>
            {
                throw new PostgresException(
                    "database failure",
                    "ERROR",
                    "ERROR",
                    ++attempts == 1 ? "57P03" : "42501"
                );
            })
            .BuildServiceProvider();
        using var runner = Runner(services);
        await runner.StartAsync(default);
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            runner.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10))
        );
        Assert.Equal("42501", failure.SqlState);
        Assert.Equal(2, attempts);
    }

    private static MigrationRunner Runner(IServiceProvider services) =>
        new(
            services,
            new Lifetime(),
            new ConfigurationBuilder().Build(),
            NullLogger<MigrationRunner>.Instance
        );

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }
}
