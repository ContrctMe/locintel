using System.Net.Http.Json;
using LocIntel.Modules.Ingest.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Secrets;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Ingest;

public sealed record SyncSiteConnector(Guid ConnectorId)
{
    // Serialized with the work request: delivery retries use the same staged batch.
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
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"SyncSiteConnector arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );

        if (await db.Batches.AnyAsync(b => b.Id == message.BatchId, ct))
            return;
        var connector = await db
            .Connectors.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == message.ConnectorId, ct);
        if (connector is null)
            return;

        // Commit the access attempt before KMS/HTTP; failure must not erase its audit.
        await bus.InvokeForTenantAsync(
            org.Value.ToString(),
            new LocIntel.Contracts.RecordDomainAudit(
                "connector.credentials_accessed",
                System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        connector.Id,
                        connector.Name,
                        message.BatchId,
                        purpose = "sync-attempt",
                    }
                )
            ),
            ct
        );
        var apiKey = await EnvelopeCrypto.DecryptAsync(connector.EncryptedCredentials, kms, ct);

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
            .ToArray();

        await bus.InvokeForTenantAsync(
            org.Value.ToString(),
            new ApplyConnectorSync(
                connector.Id,
                message.BatchId,
                ApplyConnectorSync.SnapshotFingerprint(connector),
                rows
            ),
            ct
        );
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
