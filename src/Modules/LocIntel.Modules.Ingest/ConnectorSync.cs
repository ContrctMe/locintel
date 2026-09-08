using System.Net.Http.Json;
using System.Text.Json;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Http;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Ingest;

public sealed record SyncSiteConnector(Guid ConnectorId, Guid? AdmissionId = null)
{
    public Guid BatchId { get; init; } = Guid.CreateVersion7();
}

/// <summary>
/// Pull-connector sync (ADR 18): decrypt credentials (audited - ADR 31),
/// fetch, and land in the SAME staging core as uploads. The result is a
/// staged batch with a diff preview; commit stays explicit.
/// </summary>
public static class SyncSiteConnectorHandler
{
    [NonTransactional]
    public static async Task Handle(
        SyncSiteConnector message,
        Envelope envelope,
        ITenantContext tenant,
        IngestDbContext db,
        IKeyWrapper kms,
        IHttpClientFactory httpFactory,
        IMessageBus bus,
        IHostEnvironment environment,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SyncSiteConnector arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        if (
            message.AdmissionId is { } admission
            && !await db
                .Database.SqlQuery<bool>(
                    $"SELECT EXISTS(SELECT 1 FROM platform.capacity_reservations WHERE org_id = {org.Value} AND code = {ConnectorQueue.Code} AND id = {message.ConnectorId} AND batch_id = {admission}) AS \"Value\""
                )
                .SingleAsync(ct)
        )
            return;
        var connector = await db
            .Connectors.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == message.ConnectorId, ct);
        var completed = false;
        try
        {
            if (connector is null || await db.Batches.AnyAsync(b => b.Id == message.BatchId, ct))
            {
                completed = true;
                return;
            }
            if (
                !PublicHttp.IsAllowedUrl(
                    connector.Url,
                    environment.IsDevelopment() || environment.IsEnvironment("Testing")
                )
            )
                throw new InvalidDataException("Connector destination is prohibited.");
            await bus.InvokeForTenantAsync(
                org.Value.ToString(),
                new LocIntel.Contracts.RecordDomainAudit(
                    "connector.credentials_accessed",
                    JsonSerializer.Serialize(
                        new
                        {
                            connector.Id,
                            connector.Name,
                            message.BatchId,
                        }
                    )
                ),
                ct
            );
            var apiKey = await EnvelopeCrypto.DecryptAsync(connector.EncryptedCredentials, kms, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var http = httpFactory.CreateClient("ingest-connector");
            using var request = new HttpRequestMessage(HttpMethod.Get, connector.Url);
            request.Headers.Add("X-Api-Key", apiKey);
            using var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = await BoundedRead.ReadAsync(source, IngestLimits.MaxBytes, timeout.Token);
            using var document = JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions { MaxDepth = 16 }
            );
            if (
                document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() > IngestLimits.MaxRows
            )
                throw new InvalidDataException("Connector exceeds row limit.");
            var payload =
                document.RootElement.Deserialize<List<ConnectorSiteRecord>>()
                ?? throw new InvalidDataException("Connector returned no records.");
            if (payload.Any(r => r is null))
                throw new InvalidDataException("Connector contains a null record.");
            var rows = payload
                .Select(r => new SourceRow(
                    r.ExternalId ?? "",
                    r.Name ?? "",
                    r.TimeZone ?? "",
                    r.Node ?? "",
                    r.Status ?? "open"
                ))
                .ToList();
            await bus.InvokeForTenantAsync(
                org.Value.ToString(),
                new ApplyConnectorSync(
                    connector.Id,
                    message.BatchId,
                    ApplyConnectorSync.SnapshotFingerprint(connector),
                    rows.ToArray(),
                    message.AdmissionId
                ),
                ct
            );
            completed = true;
        }
        catch (Exception e)
            when (e is HttpRequestException or InvalidDataException or JsonException
                || e is OperationCanceledException && !ct.IsCancellationRequested
            )
        {
            await bus.PublishAsync(
                new LocIntel.Contracts.RecordDomainAudit(
                    "connector.sync_failed",
                    JsonSerializer.Serialize(new { message.ConnectorId, error = e.GetType().Name })
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
            completed = true;
        }
        finally
        {
            if (completed && !ct.IsCancellationRequested)
                await bus.InvokeForTenantAsync(
                    org.Value.ToString(),
                    new CompleteConnectorAdmission(message.ConnectorId, message.AdmissionId),
                    ct
                );
        }
    }

    private sealed record ConnectorSiteRecord(
        [property: System.Text.Json.Serialization.JsonPropertyName("external_id")]
            string? ExternalId,
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string? Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("time_zone")] string? TimeZone,
        [property: System.Text.Json.Serialization.JsonPropertyName("node")] string? Node,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] string? Status
    );
}

public sealed record CompleteConnectorAdmission(Guid ConnectorId, Guid? AdmissionId);

public static class CompleteConnectorAdmissionHandler
{
    [Transactional(typeof(IngestDbContext))]
    public static async Task Handle(
        CompleteConnectorAdmission message,
        IngestDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        if (message.AdmissionId is not { } admission)
            return;
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("Connector completion requires a tenant.");
        await db.TakeAsync(message.ConnectorId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM platform.capacity_reservations WHERE id = {message.ConnectorId} AND org_id = {org.Value} AND code = {ConnectorQueue.Code} AND batch_id = {admission}",
            ct
        );
    }
}
