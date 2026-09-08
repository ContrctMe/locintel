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

public sealed record SyncSiteConnector(Guid ConnectorId, Guid? AdmissionId = null);

/// <summary>
/// Pull-connector sync (ADR 18): decrypt credentials (audited - ADR 31),
/// fetch, and land in the SAME staging core as uploads. The result is a
/// staged batch with a diff preview; commit stays explicit.
/// </summary>
public static class SyncSiteConnectorHandler
{
    [Transactional(typeof(IngestDbContext))]
    public static async Task Handle(
        SyncSiteConnector message,
        Envelope envelope,
        ITenantContext tenant,
        IngestDbContext db,
        StagingService staging,
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

        await db.TakeAsync(message.ConnectorId, ct);
        if (
            message.AdmissionId is { } admission
            && !await db
                .Database.SqlQuery<bool>(
                    $"SELECT EXISTS(SELECT 1 FROM platform.capacity_reservations WHERE org_id = {org.Value} AND code = {ConnectorQueue.Code} AND id = {message.ConnectorId} AND batch_id = {admission}) AS \"Value\""
                )
                .SingleAsync(ct)
        )
            return;
        var connector = await db.Connectors.FirstOrDefaultAsync(
            c => c.Id == message.ConnectorId,
            ct
        );
        try
        {
            if (connector is null)
                return;
            if (
                !PublicHttp.IsAllowedUrl(
                    connector.Url,
                    environment.IsDevelopment() || environment.IsEnvironment("Testing")
                )
            )
                throw new InvalidDataException("Connector destination is prohibited.");
            var apiKey = await EnvelopeCrypto.DecryptAsync(connector.EncryptedCredentials, kms, ct);
            await bus.PublishAsync(
                new LocIntel.Contracts.RecordDomainAudit(
                    "connector.credentials_accessed",
                    JsonSerializer.Serialize(new { connector.Id, connector.Name })
                ),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
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
            await staging.StageAsync(
                org,
                connector.CreatedByOrSystem(),
                connector.Name,
                rows,
                timeout.Token
            );
            connector.LastSyncedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
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
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                await CapacityReservations.ConsumeAsync(
                    db,
                    org,
                    ConnectorQueue.Code,
                    message.ConnectorId,
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

file static class ConnectorExtensions
{
    // connectors sync as system work; batches record the system actor
    public static Guid CreatedByOrSystem(this SiteConnector _) => Guid.Empty;
}
