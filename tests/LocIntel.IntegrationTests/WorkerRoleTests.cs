using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine.Runtime;
using Wolverine.Transports;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The image's worker role, booted from the same configuration as the api
/// against the same database: it serves the two probes the orchestrator
/// needs and none of the API surface. Before this the worker had no probe
/// at all while the production guide told operators to wire /healthz to
/// the deployed process.
/// </summary>
public class WorkerRoleTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task The_worker_serves_liveness_and_readiness_and_no_api()
    {
        using var worker = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("ROLE", "worker"));
        using var client = worker.CreateClient();

        var live = await client.GetFromJsonAsync<JsonElement>("/livez");
        Assert.Equal("worker", live.GetProperty("role").GetString());

        var ready = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(
            "worker",
            (await ready.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString()
        );

        // no HTTP surface on the worker: the api role owns it
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/sites")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/openapi/v1.json")).StatusCode
        );
    }
}

public class DependencyReadinessTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Each_role_checks_its_durable_store_and_liveness_stays_process_local()
    {
        using var api = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("ROLE", "api"));
        using var worker = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("ROLE", "worker"));
        using var apiClient = api.CreateClient();
        using var workerClient = worker.CreateClient();
        await AssertInitiallyReadyAsync(apiClient, api.Services);
        await AssertInitiallyReadyAsync(workerClient, worker.Services);

        await ExecuteAdminAsync("REVOKE SELECT ON identity.user_sessions FROM app_user");
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await apiClient.GetAsync("/healthz")).StatusCode
        );
        (await workerClient.GetAsync("/healthz")).EnsureSuccessStatusCode();
        await ExecuteAdminAsync("GRANT SELECT ON identity.user_sessions TO app_user");

        await ExecuteAdminAsync("REVOKE SELECT ON platform.sweep_runs FROM app_user");
        (await apiClient.GetAsync("/healthz")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await workerClient.GetAsync("/healthz")).StatusCode
        );
        await ExecuteAdminAsync("GRANT SELECT ON platform.sweep_runs TO app_user");

        // SELECT alone is insufficient: enqueue, acknowledge, and dead-letter
        // writes must remain usable on both roles, which both handle local queues.
        foreach (
            var table in new[]
            {
                "wolverine.wolverine_incoming_envelopes",
                "wolverine.wolverine_outgoing_envelopes",
                "wolverine.wolverine_dead_letters",
            }
        )
        foreach (var privilege in new[] { "INSERT", "UPDATE", "DELETE" })
        {
            await ExecuteAdminAsync($"REVOKE {privilege} ON {table} FROM app_user");
            try
            {
                await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
                await connection.OpenAsync();
                await using var read = new NpgsqlCommand(
                    $"SELECT 1 FROM {table} LIMIT 0",
                    connection
                );
                await read.ExecuteNonQueryAsync();
                Assert.Equal(
                    HttpStatusCode.ServiceUnavailable,
                    (await apiClient.GetAsync("/healthz")).StatusCode
                );
                Assert.Equal(
                    HttpStatusCode.ServiceUnavailable,
                    (await workerClient.GetAsync("/healthz")).StatusCode
                );
                (await workerClient.GetAsync("/livez")).EnsureSuccessStatusCode();
            }
            finally
            {
                await ExecuteAdminAsync($"GRANT {privilege} ON {table} TO app_user");
            }
        }
        (await apiClient.GetAsync("/healthz")).EnsureSuccessStatusCode();
        (await workerClient.GetAsync("/healthz")).EnsureSuccessStatusCode();

        var listener = worker
            .Services.GetRequiredService<IWolverineRuntime>()
            .Endpoints.ActiveListeners()
            .First();
        await listener.StopAndDrainAsync();
        try
        {
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                (await workerClient.GetAsync("/healthz")).StatusCode
            );
            (await workerClient.GetAsync("/livez")).EnsureSuccessStatusCode();
            (await apiClient.GetAsync("/healthz")).EnsureSuccessStatusCode();
        }
        finally
        {
            await listener.StartAsync();
        }
        (await workerClient.GetAsync("/healthz")).EnsureSuccessStatusCode();

        var localQueue = worker
            .Services.GetRequiredService<IWolverineRuntime>()
            .Endpoints.ActiveSendingAgents()
            .Where(agent => agent.IsDurable && agent.Destination.Scheme == "local")
            .OfType<IListenerCircuit>()
            .First();
        await localQueue.PauseWithDrainAsync(TimeSpan.FromMinutes(5));
        try
        {
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                (await workerClient.GetAsync("/healthz")).StatusCode
            );
            (await workerClient.GetAsync("/livez")).EnsureSuccessStatusCode();
            (await apiClient.GetAsync("/healthz")).EnsureSuccessStatusCode();
        }
        finally
        {
            await localQueue.StartAsync();
        }
        (await workerClient.GetAsync("/healthz")).EnsureSuccessStatusCode();

        await fixture.StopDatabaseAsync();

        Assert.Equal(HttpStatusCode.OK, (await apiClient.GetAsync("/livez")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await workerClient.GetAsync("/livez")).StatusCode);
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await apiClient.GetAsync("/healthz")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            (await workerClient.GetAsync("/healthz")).StatusCode
        );
    }

    private static async Task AssertInitiallyReadyAsync(
        HttpClient client,
        IServiceProvider services
    )
    {
        using var response = await client.GetAsync("/healthz");
        if (response.IsSuccessStatusCode)
            return;
        var runtime = services.GetRequiredService<IWolverineRuntime>();
        var state = services.GetRequiredService<LocIntel.Api.ReadinessState>();
        Assert.Fail(
            $"Initial readiness: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}; "
                + JsonSerializer.Serialize(
                    new
                    {
                        state.Ready,
                        cancelled = runtime.Cancellation.IsCancellationRequested,
                        queues = runtime
                            .Endpoints.ActiveSendingAgents()
                            .Select(agent => new
                            {
                                destination = agent.Destination.ToString(),
                                agent.IsDurable,
                                agent.Latched,
                                status = (agent as IListenerCircuit)?.Status.ToString(),
                            }),
                        listeners = runtime
                            .Endpoints.ActiveListeners()
                            .Select(listener => new
                            {
                                status = listener.Status.ToString(),
                                listener.ReceiverHasFaulted,
                            }),
                    }
                )
        );
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
