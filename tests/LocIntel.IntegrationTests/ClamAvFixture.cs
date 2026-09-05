using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;

namespace LocIntel.IntegrationTests;

public sealed class ClamAvFixture : ApiFixture
{
    private readonly IContainer _clamd = new ContainerBuilder("clamav/clamav:1.5.4-debian")
        .WithPortBinding(3310, assignRandomHostPort: true)
        // Use the image's bundled signatures; this does not test signature updates.
        .WithEnvironment("CLAMAV_NO_FRESHCLAMD", "true")
        .WithWaitStrategy(
            Wait.ForUnixContainer()
                .UntilCommandIsCompleted(
                    "clamdcheck.sh",
                    w => w.WithTimeout(TimeSpan.FromMinutes(3))
                )
        )
        .Build();

    public override async Task InitializeAsync()
    {
        await _clamd.StartAsync();
        await base.InitializeAsync();
    }

    protected override void ConfigureHost(IWebHostBuilder builder)
    {
        base.ConfigureHost(builder);
        builder.UseSetting("Scanner:Provider", "clamav");
        builder.UseSetting("Scanner:ClamAv:Host", _clamd.Hostname);
        builder.UseSetting("Scanner:ClamAv:Port", _clamd.GetMappedPublicPort(3310).ToString());
        builder.UseSetting("Scanner:ClamAv:TimeoutSeconds", "5");
    }

    // Pause keeps Docker's random host port stable while clamd cannot answer.
    public Task PauseScannerAsync() => _clamd.PauseAsync();

    public Task ResumeScannerAsync() => _clamd.UnpauseAsync();

    public override async Task DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            await _clamd.DisposeAsync();
        }
    }
}
