using LocIntel.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace LocIntel.IntegrationTests;

public class RequestCancellationTests
{
    [Theory]
    [InlineData(true, 499)]
    [InlineData(false, 500)]
    public async Task Only_request_aborts_are_classified_as_client_disconnects(
        bool requestAborted,
        int expectedStatus
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseMiddleware<UnhandledErrorMiddleware>();
        app.Run(context =>
        {
            // Exercise the real HTTP middleware with a cancelled
            // request versus an independent operation cancellation.
            context.RequestAborted = new CancellationToken(requestAborted);
            throw new OperationCanceledException();
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (requestAborted)
            Assert.Empty(body);
        else
        {
            Assert.Contains("something went wrong on our side", body);
            Assert.True(response.Headers.Contains("X-Trace-Id"));
        }
    }
}
