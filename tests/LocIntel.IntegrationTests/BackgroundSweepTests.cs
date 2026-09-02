using LocIntel.Platform.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Background sweeps only register in the WORKER role, so nothing else in
/// this suite resolves their dependencies - a missing registration would
/// surface as a production worker crashing on its first tick, hours after
/// deploy. These assert the graph directly.
/// </summary>
public class BackgroundSweepTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public void The_per_org_sweep_port_resolves()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var enumerator = scope.ServiceProvider.GetRequiredService<IOrganizationEnumerator>();
        Assert.NotNull(enumerator);
    }

    [Fact]
    public async Task The_sweep_port_lists_the_orgs_a_sweep_would_fan_out_to()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var enumerator = scope.ServiceProvider.GetRequiredService<IOrganizationEnumerator>();
        var ids = await enumerator.ListIdsAsync();
        Assert.Contains(fixture.OrgA, ids);
        Assert.Contains(fixture.OrgB, ids);
    }
}
