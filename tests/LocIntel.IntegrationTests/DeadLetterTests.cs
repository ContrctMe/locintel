using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

/// <summary>
/// The operator's dead-letter surface (operability item 1): a real failure
/// round trip - a message whose handler throws lands in the store, gets
/// listed with its exception and tenant, replays (re-executes and re-fails,
/// proving replay is real), and discards.
/// </summary>
public class DeadLetterTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    /// <summary>A DirectoryUserSynced with no tenant on the envelope throws by design.</summary>
    private async Task PublishDoomedMessageAsync()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(
            new LocIntel.Modules.Identity.Users.DirectoryUserSynced(
                LocIntel.Platform.Auth.DirectorySyncKind.UserUpserted,
                "doomed@example.com",
                null
            )
        );
    }

    private async Task<JsonElement?> FindAsync(HttpClient op, string exceptionFragment)
    {
        var page = await op.GetFromJsonAsync<JsonElement>("/api/operator/dead-letters");
        foreach (var item in page.GetProperty("items").EnumerateArray())
            if (
                item.GetProperty("exceptionMessage").GetString()!.Contains(exceptionFragment)
                && item.GetProperty("messageType").GetString() == "DirectoryUserSynced"
            )
                return item;
        return null;
    }

    [Fact]
    public async Task Failed_message_is_listed_replayed_and_discarded()
    {
        var op = await fixture.OperatorClient();
        await PublishDoomedMessageAsync();

        JsonElement? dead = null;
        for (var i = 0; i < 200 && dead is null; i++)
        {
            dead = await FindAsync(op, "no tenant");
            if (dead is null)
                await Task.Delay(100);
        }
        Assert.NotNull(dead);
        Assert.Equal(
            "InvalidOperationException",
            dead.Value.GetProperty("exceptionType").GetString()
        );

        // replay re-executes the handler; it fails identically, so the same
        // message id lands BACK in the store - proof the replay is real
        var id = dead.Value.GetProperty("id").GetGuid();
        var replayed = await op.PostAsync($"/api/operator/dead-letters/{id}/replay", null);
        Assert.Equal(HttpStatusCode.Accepted, replayed.StatusCode);
        var reFailed = false;
        for (var i = 0; i < 300 && !reFailed; i++)
        {
            var again = await FindAsync(op, "no tenant");
            reFailed = again is { } a && !a.GetProperty("replayable").GetBoolean();
            if (!reFailed)
                await Task.Delay(100);
        }
        Assert.True(reFailed, "replayed message never re-failed back into the store");

        // discard: gone for good
        var current = await FindAsync(op, "no tenant");
        var discardId = current!.Value.GetProperty("id").GetGuid();
        var discarded = await op.DeleteAsync($"/api/operator/dead-letters/{discardId}");
        Assert.Equal(HttpStatusCode.NoContent, discarded.StatusCode);
        var gone = false;
        for (var i = 0; i < 100 && !gone; i++)
        {
            gone = await FindAsync(op, "no tenant") is null;
            if (!gone)
                await Task.Delay(100);
        }
        Assert.True(gone);
    }

    [Fact]
    public async Task Dead_letters_are_operator_only()
    {
        var owner = await fixture.LoginAsync(ApiFixture.UserA);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/operator/dead-letters")).StatusCode
        );
    }
}
