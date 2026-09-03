using LocIntel.Contracts;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace LocIntel.IntegrationTests;

/// <summary>
/// FanOutAsync through the REAL outbox (ADR 48, docs/cross-tenant-sharing.md).
/// SendOrgNotice is the probe because its handler resolves recipients from
/// the ENVELOPE's org - so a notice landing in each org's managers' mail is
/// direct proof that each copy ran under its own tenant.
/// </summary>
public class FanOutTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Each_org_in_the_list_receives_its_own_tenanted_copy_exactly_once()
    {
        // IMessageBus is scoped: publishing from outside a handler needs a scope
        using var scope = fixture.Factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var catcher = fixture.Factory.Services.GetRequiredService<LocalMailCatcher>();
        var subject = $"fan-out {Guid.NewGuid():N}";

        // Org A appears twice on purpose: one recipient, one copy
        await bus.FanOutAsync(
            [fixture.OrgA, fixture.OrgB, fixture.OrgA],
            new SendOrgNotice(subject, ["This reached you through a fan-out."]),
            correlationId: Guid.NewGuid()
        );

        await ApiFixture.WaitUntilAsync(
            async () =>
            {
                await Task.CompletedTask;
                var to = catcher.Sent.Where(m => m.Subject == subject).Select(m => m.To).ToList();
                return to.Contains(ApiFixture.UserA) && to.Contains(ApiFixture.UserB);
            },
            "managers of BOTH orgs to receive the fanned-out notice",
            diagnostics: fixture.DeadLetterSummary
        );

        var delivered = catcher.Sent.Where(m => m.Subject == subject).ToList();
        // A's manager got it once despite A being listed twice
        Assert.Single(delivered, m => m.To == ApiFixture.UserA);
        Assert.Single(delivered, m => m.To == ApiFixture.UserB);
        // and nobody outside the list - the operator's org was not fanned to
        Assert.DoesNotContain(delivered, m => m.To == ApiFixture.Operator);
    }
}
