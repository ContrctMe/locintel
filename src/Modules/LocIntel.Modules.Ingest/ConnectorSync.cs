using System.Net.Http.Json;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Secrets;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Ingest;

public sealed record SyncSiteConnector(Guid ConnectorId);

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
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SyncSiteConnector arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        var connector = await db.Connectors.FirstOrDefaultAsync(
            c => c.Id == message.ConnectorId,
            ct
        );
        if (connector is null)
            return;

        var apiKey = await EnvelopeCrypto.DecryptAsync(connector.EncryptedCredentials, kms, ct);
        await bus.PublishAsync(
            new LocIntel.Contracts.RecordDomainAudit(
                "connector.credentials_accessed",
                System.Text.Json.JsonSerializer.Serialize(new { connector.Id, connector.Name })
            ),
            new DeliveryOptions { TenantId = org.Value.ToString() }
        );

        using var http = httpFactory.CreateClient("ingest-connector");
        using var request = new HttpRequestMessage(HttpMethod.Get, connector.Url);
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var payload =
            await response.Content.ReadFromJsonAsync<List<ConnectorSiteRecord>>(ct)
            ?? throw new InvalidOperationException("connector returned no parseable payload");
        var rows = payload
            .Select(r => new SourceRow(
                r.ExternalId ?? "",
                r.Name ?? "",
                r.TimeZone ?? "",
                r.Node ?? "",
                r.Status ?? "open"
            ))
            .ToList();

        await staging.StageAsync(org, connector.CreatedByOrSystem(), connector.Name, rows, ct);
        connector.LastSyncedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
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
