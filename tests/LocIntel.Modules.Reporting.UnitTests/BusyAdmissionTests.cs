using System.Text.Json;
using LocIntel.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.Modules.Reporting.UnitTests;

/// <summary>
/// Report admission refuses contention rather than waiting on a pooled
/// connection (ADR 55), so the refusal has to say when to come back: without a
/// Retry-After the console cannot tell this apart from a 503 raised by
/// something in front of the API, after a write already landed.
/// </summary>
public sealed class BusyAdmissionTests
{
    // Every refusal writes its body through Results.Json for one shape, and that
    // reads its options from request services. A real container of real
    // framework services: nothing here stands in for anything.
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .BuildServiceProvider();

    private static DefaultHttpContext Context() =>
        new() { RequestServices = Services, Response = { Body = new MemoryStream() } };

    [Fact]
    public async Task A_busy_admission_is_a_timed_503_with_the_shared_refusal_body()
    {
        var context = Context();

        await ApiErrors.Busy("Another report is being admitted.", 2).ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("2", context.Response.Headers.RetryAfter);

        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        Assert.Equal("Another report is being admitted.", body.GetProperty("error").GetString());
        Assert.Equal("admission_busy", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task It_names_a_delay_even_when_the_caller_does_not()
    {
        var context = Context();

        await ApiErrors.Busy("Busy.").ExecuteAsync(context);

        Assert.Equal("1", context.Response.Headers.RetryAfter);
    }
}
