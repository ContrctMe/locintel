using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocIntel.Modules.Reporting;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Modules.Tenancy.Sites;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

/// <summary>A concrete test report exercises the fork registration contract. PDF layout is tested separately.</summary>
public sealed class WorkflowReportDefinition(ConcurrentDictionary<Guid, int> attempts)
    : IReportDefinition
{
    private readonly ConcurrentDictionary<Guid, int> _attempts = attempts;
    public string Id => "workflow-test";
    public string Name => "Workflow test";
    public int Version => 1;
    public bool Aggregate => false;

    public string? ValidateOptions(JsonElement options) => null;

    /// <summary>
    /// Definition-level refusal, which only this contract can express: the
    /// dependency argument is null at admission and generation and present at
    /// download, so "denyAfterGeneration" allows the work and then refuses to
    /// hand back what it produced.
    /// </summary>
    public Task<bool> AuthorizeAsync(
        IReportDefinition.Request request,
        JsonElement? dependencies,
        CancellationToken ct
    ) =>
        Task.FromResult(
            dependencies is null
                || !request.Options.TryGetProperty("denyAfterGeneration", out var deny)
                || !deny.GetBoolean()
        );

    public async Task<IReportDefinition.Output> RenderAsync(
        IReportDefinition.Request request,
        Stream destination,
        CancellationToken ct
    )
    {
        var id = request.SiteIds.Single();
        var attempt = _attempts.AddOrUpdate(id, 1, (_, n) => n + 1);
        if (
            request.Options.TryGetProperty("failFirstSite", out var fail)
            && fail.GetGuid() == id
            && attempt == 1
        )
            throw new IOException("Test first-attempt failure.");
        if (request.Options.TryGetProperty("hold", out var hold) && hold.GetBoolean())
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        if (request.Options.TryGetProperty("block", out var block) && block.GetBoolean())
        {
            // Ignore the token exactly as a synchronous rendering library does,
            // then try to write anyway. The executor must already have given up.
            Thread.Sleep(TimeSpan.FromSeconds(5));
            await destination.WriteAsync(
                Encoding.UTF8.GetBytes("late bytes"),
                CancellationToken.None
            );
        }
        if (request.Options.TryGetProperty("oversize", out var oversize) && oversize.GetBoolean())
        {
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < 21; i++)
                await destination.WriteAsync(chunk, ct);
        }
        await destination.WriteAsync(
            Encoding.UTF8.GetBytes($"%PDF-1.7\nworkflow-fixture-{id}-{attempt}\n%%EOF"),
            ct
        );
        return new(JsonSerializer.SerializeToElement(new { sites = request.SiteIds }), []);
    }
}
