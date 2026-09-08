using System.IO.Compression;
using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wolverine.EntityFrameworkCore;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// Durable job state lives in Postgres; Wolverine delivery and the recovery sweep
/// can both resume it. Only short metadata operations hold a transaction/row lease.
/// </summary>
public sealed class ReportRunner(
    IServiceScopeFactory scopeFactory,
    ReportLimits limits,
    TimeProvider time,
    ILogger<ReportRunner> logger
)
{
    public async Task RunAsync(OrgId org, RegionId region, Guid id, CancellationToken ct)
    {
        var owner = Guid.CreateVersion7();
        var claim = await Change(
            org,
            region,
            id,
            async (db, job) =>
            {
                if (
                    job.State is not (ReportJobState.Queued or ReportJobState.Running)
                    || job.LeaseUntil > time.GetUtcNow()
                    || job.ExpiresAt <= time.GetUtcNow()
                )
                    return false;
                job.State = ReportJobState.Running;
                job.LeaseOwner = owner;
                job.LeaseUntil = time.GetUtcNow().AddSeconds(limits.ItemTimeoutSeconds + 60);
                foreach (var item in job.Items.Where(x => x.State == ReportItemState.Running))
                    item.State = ReportItemState.Queued;
                await db.SaveChangesAsync(ct);
                return true;
            },
            ct
        );
        if (!claim)
            return;
        var siteNames = await SiteNames(org, region, id, ct);
        logger.LogInformation(
            "Report {JobId} claimed by process {ProcessId}",
            id,
            Environment.ProcessId
        );
        try
        {
            while (true)
            {
                var job = await Read(org, region, id, ct);
                if (job is null || job.State != ReportJobState.Running || job.LeaseOwner != owner)
                    return;
                var item = job.Items.FirstOrDefault(x => x.State == ReportItemState.Queued);
                if (item is null)
                {
                    await Complete(job, owner, region, siteNames, ct);
                    return;
                }
                var artifact = new ReportArtifact
                {
                    OrgId = org,
                    JobId = id,
                    ItemId = item.Id,
                    Revision = job.Revision,
                    Key = "",
                    Name = ReportFileNames.Pdf(item.SiteIds, siteNames, time.GetUtcNow()),
                    ContentType = "application/pdf",
                };
                // The object identity is unique per attempt; a stale worker cannot overwrite a newer PDF.
                artifact = WithKey(artifact, region);
                if (
                    !await Change(
                        org,
                        region,
                        id,
                        async (db, current) =>
                        {
                            if (!Owns(current, job.Revision, owner))
                                return false;
                            var row = current.Items.Single(x => x.Id == item.Id);
                            row.State = ReportItemState.Running;
                            row.Attempt++;
                            current.LeaseUntil = time.GetUtcNow()
                                .AddSeconds(limits.ItemTimeoutSeconds + 60);
                            db.Artifacts.Add(artifact);
                            await db.SaveChangesAsync(ct);
                            return true;
                        },
                        ct
                    )
                )
                    return;

                IReportDefinition.Output? result = null;
                string? error = null;
                long bytes = 0;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(limits.ItemTimeoutSeconds));
                var cancellation = WatchCancellation(org, region, id, owner, timeout);
                try
                {
                    using var scope = Scope(org, region);
                    var definition = scope
                        .ServiceProvider.GetRequiredService<ReportRegistry>()
                        .Find(job.ReportType, job.DefinitionVersion);
                    if (definition is null)
                        error = "definition_unavailable";
                    else if (
                        !await scope
                            .ServiceProvider.GetRequiredService<ReportAccess>()
                            .CanAccessSitesAsync(org, job.RequestedBy, item.SiteIds, timeout.Token)
                        || !await definition.AuthorizeAsync(
                            ReportAccess.Request(job, item.SiteIds),
                            null,
                            timeout.Token
                        )
                    )
                        error = "access_lost";
                    else
                    {
                        bytes = await Store(
                            scope.ServiceProvider.GetRequiredService<IObjectStore>(),
                            artifact,
                            limits.MaxPdfBytes,
                            async stream =>
                                result = await Render(definition, job, item, stream, timeout.Token),
                            timeout.Token
                        );
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    error = "generation_timeout";
                }
                catch (Exception e)
                {
                    error = "generation_failed";
                    // Exception messages may contain provider URLs; record only the exception type.
                    logger.LogWarning(
                        "Report {JobId} item {ItemId} failed ({ErrorType})",
                        id,
                        item.Id,
                        e.GetType().Name
                    );
                }
                finally
                {
                    await timeout.CancelAsync();
                    await cancellation;
                }
                if (
                    !await Change(
                        org,
                        region,
                        id,
                        async (db, current) =>
                        {
                            if (!Owns(current, job.Revision, owner))
                                return false;
                            var row = current.Items.Single(x => x.Id == item.Id);
                            row.State =
                                error is null && result is not null
                                    ? ReportItemState.Succeeded
                                    : ReportItemState.Failed;
                            await ReportQuota.SettleAsync(
                                db,
                                org,
                                item.Id,
                                row.State == ReportItemState.Succeeded,
                                ct
                            );
                            row.ErrorCode = error ?? (result is null ? "generation_failed" : null);
                            row.GeneratedAt = time.GetUtcNow();
                            row.WarningsJson = "[]";
                            row.DependenciesJson = "{}";
                            if (error is null && result is not null)
                            {
                                row.WarningsJson = JsonSerializer.Serialize(result.Warnings);
                                row.DependenciesJson = result.Dependencies.GetRawText();
                                var stored = current.Artifacts.Single(x => x.Id == artifact.Id);
                                stored.Ready = error is null;
                                stored.Bytes = bytes;
                            }
                            await db.SaveChangesAsync(ct);
                            return true;
                        },
                        ct
                    )
                )
                    return;
            }
        }
        finally
        {
            // On shutdown, leave a resumable Running job; delivery/sweep retries it.
            await Change(
                org,
                region,
                id,
                async (db, job) =>
                {
                    if (job.LeaseOwner != owner)
                        return false;
                    job.LeaseOwner = null;
                    job.LeaseUntil = null;
                    await db.SaveChangesAsync(CancellationToken.None);
                    return true;
                },
                CancellationToken.None
            );
        }
    }

    /// <summary>
    /// Every site in the run, named once. Organization scope is correct here: the
    /// requester's access was checked at admission and is rechecked per item before
    /// rendering, and these names only ever reach files those same sites own.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> SiteNames(
        OrgId org,
        RegionId region,
        Guid id,
        CancellationToken ct
    )
    {
        var job = await Read(org, region, id, ct);
        if (job is null || job.SiteIds.Length == 0)
            return new Dictionary<Guid, string>();
        using var scope = Scope(org, region);
        var sites = await scope
            .ServiceProvider.GetRequiredService<ISiteSource>()
            .SelectAsync(org, new NodeScope.EntireOrg(org), job.SiteIds, job.SiteIds.Length, ct);
        return sites.ToDictionary(x => x.Id, x => x.Name);
    }

    private async Task Complete(
        ReportJob job,
        Guid owner,
        RegionId region,
        IReadOnlyDictionary<Guid, string> siteNames,
        CancellationToken ct
    )
    {
        var succeeded = job.Items.Where(x => x.State == ReportItemState.Succeeded).ToArray();
        var state =
            succeeded.Length == 0 ? ReportJobState.Failed
            : succeeded.Length == job.Items.Count ? ReportJobState.Completed
            : ReportJobState.CompletedWithErrors;
        string? error = null;
        // A crash can occur after the ZIP is committed but before the job is finished.
        // Keep that revision's published bundle instead of producing a second artifact.
        if (
            job.Mode == ReportMode.Bulk
            && succeeded.Length > 0
            && !job.Artifacts.Any(x => x.ItemId == null && x.Ready && x.Revision == job.Revision)
        )
        {
            var artifact = WithKey(
                new ReportArtifact
                {
                    OrgId = job.OrgId,
                    JobId = job.Id,
                    Revision = job.Revision,
                    Key = "",
                    Name = ReportFileNames.Zip(job.SiteIds.Length, time.GetUtcNow()),
                    ContentType = "application/zip",
                },
                region
            );
            if (
                !await Change(
                    job.OrgId,
                    region,
                    job.Id,
                    async (db, current) =>
                    {
                        if (!Owns(current, job.Revision, owner))
                            return false;
                        current.LeaseUntil = time.GetUtcNow()
                            .AddSeconds(limits.ItemTimeoutSeconds + 60);
                        db.Artifacts.Add(artifact);
                        await db.SaveChangesAsync(ct);
                        return true;
                    },
                    ct
                )
            )
                return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.ItemTimeoutSeconds));
            var cancellation = WatchCancellation(job.OrgId, region, job.Id, owner, timeout);
            try
            {
                using var scope = Scope(job.OrgId, region);
                var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
                var bytes = await Store(
                    store,
                    artifact,
                    limits.MaxZipBytes,
                    async stream =>
                    {
                        using var zip = new ZipArchive(
                            stream,
                            ZipArchiveMode.Create,
                            leaveOpen: true
                        );
                        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in succeeded)
                        {
                            var pdf = job.Artifacts.Single(x => x.ItemId == item.Id && x.Ready);
                            await using var input = await store.OpenReadAsync(
                                pdf.Key,
                                timeout.Token
                            );
                            await using var entry = zip.CreateEntry(
                                    ReportFileNames.Unique(taken, pdf.Name),
                                    CompressionLevel.Fastest
                                )
                                .Open();
                            await input.CopyToAsync(entry, timeout.Token);
                        }
                        await using var manifest = zip.CreateEntry("manifest.json").Open();
                        await JsonSerializer.SerializeAsync(
                            manifest,
                            new
                            {
                                job.Id,
                                job.ReportType,
                                job.DefinitionVersion,
                                job.Selection,
                                items = job.Items.Select(x => new
                                {
                                    x.Id,
                                    x.SiteIds,
                                    // Named, so a manifest read outside the console
                                    // still says which site each PDF covers.
                                    sites = x.SiteIds.Select(s => new
                                    {
                                        id = s,
                                        name = siteNames.GetValueOrDefault(s),
                                    }),
                                    x.State,
                                    x.ErrorCode,
                                    x.GeneratedAt,
                                    warnings = JsonSerializer.Deserialize<string[]>(x.WarningsJson),
                                }),
                            },
                            cancellationToken: timeout.Token
                        );
                    },
                    timeout.Token
                );
                await Change(
                    job.OrgId,
                    region,
                    job.Id,
                    async (db, current) =>
                    {
                        if (!Owns(current, job.Revision, owner))
                            return false;
                        var stored = current.Artifacts.Single(x => x.Id == artifact.Id);
                        stored.Ready = true;
                        stored.Bytes = bytes;
                        await db.SaveChangesAsync(ct);
                        return true;
                    },
                    ct
                );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                state = ReportJobState.Failed;
                error = "bundle_failed";
                logger.LogWarning(
                    "Report {JobId} bundle failed ({ErrorType})",
                    job.Id,
                    e.GetType().Name
                );
            }
            finally
            {
                await timeout.CancelAsync();
                await cancellation;
            }
        }
        await Change(
            job.OrgId,
            region,
            job.Id,
            async (db, current) =>
            {
                if (!Owns(current, job.Revision, owner))
                    return false;
                current.ErrorCode = error;
                ReportEndpoints.Finish(current, state, limits, time.GetUtcNow());
                await db.SaveChangesAsync(ct);
                return true;
            },
            ct
        );
    }

    /// <summary>
    /// Render under a real deadline. A definition may spend its whole budget
    /// inside one synchronous library call that never observes the token, so
    /// awaiting it directly made ItemTimeoutSeconds advisory: the lease
    /// expired, another worker re-claimed the item, and the same PDF was
    /// produced twice with two settlements to reconcile. The render runs on
    /// its own thread and is abandoned when the deadline passes; the bounded
    /// stream then refuses its remaining writes. A render stuck in pure
    /// computation still runs to completion on that thread - it cannot be
    /// killed - but nothing waits for it and nothing it writes is published.
    /// </summary>
    private static async Task<IReportDefinition.Output> Render(
        IReportDefinition definition,
        ReportJob job,
        ReportItem item,
        Stream stream,
        CancellationToken ct
    )
    {
        var render = Task.Run(
            () => definition.RenderAsync(ReportAccess.Request(job, item.SiteIds), stream, ct),
            CancellationToken.None
        );
        // An abandoned render faults on its next write; observe it so it is
        // not reported as an unobserved task exception.
        _ = render.ContinueWith(
            static task => task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
        return await render.WaitAsync(ct);
    }

    private static bool Owns(ReportJob job, int revision, Guid owner) =>
        job.State == ReportJobState.Running && job.Revision == revision && job.LeaseOwner == owner;

    private async Task WatchCancellation(
        OrgId org,
        RegionId region,
        Guid id,
        Guid owner,
        CancellationTokenSource stop
    )
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                var job = await Read(org, region, id, stop.Token);
                if (job is null || job.State != ReportJobState.Running || job.LeaseOwner != owner)
                {
                    await stop.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception)
        {
            // Without a current lease/cancellation check, stop producing bytes.
            await stop.CancelAsync();
        }
    }

    private static ReportArtifact WithKey(ReportArtifact artifact, RegionId region) =>
        new()
        {
            Id = artifact.Id,
            OrgId = artifact.OrgId,
            JobId = artifact.JobId,
            ItemId = artifact.ItemId,
            Revision = artifact.Revision,
            Name = artifact.Name,
            ContentType = artifact.ContentType,
            Key =
                $"{region.Value}/{artifact.OrgId.Value}/reports/{artifact.JobId:N}/{artifact.Id:N}",
        };

    private static async Task<long> Store(
        IObjectStore store,
        ReportArtifact artifact,
        long limit,
        Func<Stream, Task> render,
        CancellationToken ct
    )
    {
        var path = Path.Combine(Path.GetTempPath(), "locintel-report-" + artifact.Id.ToString("N"));
        await using var file = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            65536,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose
        );
        using var bounded = new ReportWriteStream(file, limit);
        try
        {
            await render(bounded);
        }
        finally
        {
            // Past this point the render may still be running; its writes must
            // not reach the file this method is about to close.
            bounded.Abandon();
        }
        ct.ThrowIfCancellationRequested();
        if (file.Length == 0)
            throw new IOException("Report output is empty.");
        file.Position = 0;
        await store.WriteAsync(artifact.Key, file, artifact.ContentType, ct);
        return file.Length;
    }

    private IServiceScope Scope(OrgId org, RegionId region)
    {
        var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(org, region);
        return scope;
    }

    private async Task<ReportJob?> Read(OrgId org, RegionId region, Guid id, CancellationToken ct)
    {
        using var scope = Scope(org, region);
        return await scope
            .ServiceProvider.GetRequiredService<ReportingDbContext>()
            .Jobs.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.OrgId == org && x.Id == id, ct);
    }

    private async Task<bool> Change(
        OrgId org,
        RegionId region,
        Guid id,
        Func<ReportingDbContext, ReportJob, Task<bool>> action,
        CancellationToken ct
    )
    {
        using var scope = Scope(org, region);
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.TakeAsync(id, ct);
        var job = await db
            .Jobs.Include(x => x.Items)
            .Include(x => x.Artifacts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.OrgId == org && x.Id == id, ct);
        if (job is null)
            return false;
        var readyBefore = job.Artifacts.Where(x => x.Ready).Select(x => x.Id).ToHashSet();
        var outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox>();
        outbox.Enroll(db);
        var result = await action(db, job);
        foreach (
            var artifact in job.Artifacts.Where(x =>
                x.Ready && x.ItemId != null && !readyBefore.Contains(x.Id)
            )
        )
        {
            var item = job.Items.Single(x => x.Id == artifact.ItemId);
            await outbox.PublishForOrgAsync(
                org,
                new PublishGeneratedFile(
                    artifact.Id,
                    artifact.Key,
                    artifact.Name,
                    artifact.ContentType,
                    artifact.Bytes,
                    job.RequestedBy,
                    item.GeneratedAt ?? artifact.CreatedAt,
                    item.SiteIds,
                    "report",
                    job.Id
                )
            );
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return result;
    }
}
