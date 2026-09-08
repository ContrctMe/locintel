using System.Collections.Concurrent;
using System.Data.Common;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Modules.Storage;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace LocIntel.IntegrationTests;

public class ScanLifetimeFixture : ApiFixture
{
    public ScanProbe Probe { get; } = new();

    protected override void ConfigureHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(Probe).AddSingleton<IVirusScanner, ConnectionCheckingScanner>();
            services.AddSingleton<LocalObjectStore>();
            services.AddSingleton<IObjectStore, ProbingObjectStore>();
            services.ConfigureDbContext<StorageDbContext>(options =>
                options.AddInterceptors(new FailingScanCommit(Probe), new ObserveConnections(Probe))
            );
            services.ConfigureDbContext<IngestDbContext>(options =>
                options.AddInterceptors(new ObserveConnections(Probe), new FailingScanCommit(Probe))
            );
        });

    public class ScanProbe
    {
        public Func<Task>? DuringScan { get; set; }
        public Guid? FailCommitFor { get; set; }
        public Guid? FailBatchCommitFor { get; set; }
        public ConcurrentDictionary<Guid, byte> OpenConnections { get; } = new();
        public int ConnectionsObserved;
        public Func<Task>? BeforeRead { get; set; }
        public Func<Task>? BeforePreviewWrite { get; set; }
    }

    public class ProbingObjectStore(LocalObjectStore inner, ScanProbe probe) : IObjectStore
    {
        public ValueTask<UploadTicket> CreateUploadTicketAsync(
            string key,
            string contentType,
            long maxBytes,
            CancellationToken ct = default
        ) => inner.CreateUploadTicketAsync(key, contentType, maxBytes, ct);

        public ValueTask<Uri> GetDownloadUrlAsync(
            string key,
            TimeSpan ttl,
            CancellationToken ct = default
        ) => inner.GetDownloadUrlAsync(key, ttl, ct);

        public ValueTask<long?> GetLengthAsync(string key, CancellationToken ct = default) =>
            inner.GetLengthAsync(key, ct);

        public async ValueTask<Stream> OpenReadAsync(string key, CancellationToken ct = default)
        {
            if (probe.BeforeRead is { } action)
                await action();
            return await inner.OpenReadAsync(key, ct);
        }

        public async ValueTask WriteAsync(
            string key,
            Stream content,
            string contentType,
            CancellationToken ct = default
        )
        {
            if (key.EndsWith(".preview.txt") && probe.BeforePreviewWrite is { } action)
                await action();
            await inner.WriteAsync(key, content, contentType, ct);
        }

        public ValueTask DeleteAsync(string key, CancellationToken ct = default) =>
            inner.DeleteAsync(key, ct);
    }

    public class ConnectionCheckingScanner(ScanProbe probe) : IVirusScanner
    {
        public async ValueTask<ScanVerdict> ScanAsync(
            Stream content,
            CancellationToken ct = default
        )
        {
            Assert.True(Volatile.Read(ref probe.ConnectionsObserved) > 0);
            Assert.Empty(probe.OpenConnections);
            if (probe.DuringScan is { } action)
                await action();
            return ScanVerdict.Clean;
        }
    }

    private class ObserveConnections(ScanProbe probe) : DbConnectionInterceptor
    {
        public override void ConnectionOpened(
            DbConnection connection,
            ConnectionEndEventData eventData
        )
        {
            Interlocked.Increment(ref probe.ConnectionsObserved);
            probe.OpenConnections[eventData.ConnectionId] = 0;
        }

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            ConnectionOpened(connection, eventData);
            return Task.CompletedTask;
        }

        public override void ConnectionClosed(
            DbConnection connection,
            ConnectionEndEventData eventData
        ) => probe.OpenConnections.TryRemove(eventData.ConnectionId, out _);

        public override Task ConnectionClosedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData
        )
        {
            ConnectionClosed(connection, eventData);
            return Task.CompletedTask;
        }
    }

    private class FailingScanCommit(ScanProbe probe) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                probe.FailBatchCommitFor is { } batchId
                && eventData.Context is IngestDbContext ingest
                && ingest.ChangeTracker.Entries<ImportBatch>().Any(e => e.Entity.Id == batchId)
            )
                throw new InvalidOperationException("injected batch commit failure");
            if (
                probe.FailCommitFor is { } id
                && eventData.Context is StorageDbContext db
                && db.ChangeTracker.Entries<FileObject>()
                    .Any(e => e.Entity.Id == id && e.Entity.ScannedAt != null)
            )
                throw new InvalidOperationException("injected scan commit failure");
            return ValueTask.FromResult(result);
        }
    }
}
