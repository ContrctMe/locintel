using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Secrets;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Audit;

/// <summary>One delivery attempt for one endpoint; the org rides the envelope.</summary>
public sealed record DeliverWebhook(
    Guid EndpointId,
    Guid DeliveryGroupId,
    string EventName,
    string PayloadJson,
    DateTimeOffset EventAt,
    int Attempt
);

/// <summary>
/// Delivers a domain event to one subscribed endpoint (ADR 40): JSON body,
/// Stripe-style signature (t=...,v1=hmacsha256(secret, "{t}.{body}")) - the
/// exact scheme the template VERIFIES on its own inbound billing webhooks,
/// so both directions share one convention. Non-2xx or transport failure
/// records the attempt and self-schedules a retry with exponential backoff,
/// up to five attempts. Every attempt is a tenant-visible delivery row.
/// </summary>
public static class DeliverWebhookHandler
{
    public const int MaxAttempts = 5;

    [Transactional(typeof(AuditDbContext))]
    public static async Task Handle(
        DeliverWebhook message,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        IKeyWrapper kms,
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"webhook delivery arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        var endpoint = await db.WebhookEndpoints.FirstOrDefaultAsync(
            e => e.Id == message.EndpointId && e.Active,
            ct
        );
        if (endpoint is null)
            return; // deleted or paused between event and delivery: drop quietly

        var body = JsonSerializer.Serialize(
            new
            {
                id = message.DeliveryGroupId,
                @event = message.EventName,
                occurredAt = message.EventAt,
                payload = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(message.PayloadJson) ? "{}" : message.PayloadJson
                ),
                attempt = message.Attempt,
            }
        );
        var secret = await EnvelopeCrypto.DecryptAsync(endpoint.EncryptedSecret, kms, ct);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signatureHeader = $"t={timestamp},v1={Sign(secret, timestamp, body)}";
        // rotation's dual-secret window: a second v1 entry signed with the
        // OLD secret, so consumers verifying against either one succeed
        if (
            endpoint.PreviousEncryptedSecret is { } previousCiphertext
            && endpoint.PreviousSecretExpiresAt > DateTimeOffset.UtcNow
        )
        {
            var previous = await EnvelopeCrypto.DecryptAsync(previousCiphertext, kms, ct);
            signatureHeader += $",v1={Sign(previous, timestamp, body)}";
        }

        int? statusCode = null;
        var ok = false;
        try
        {
            using var http = httpFactory.CreateClient("webhook-delivery");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-LocIntel-Signature", signatureHeader);
            request.Headers.Add("X-LocIntel-Event", message.EventName);
            using var response = await http.SendAsync(request, ct);
            statusCode = (int)response.StatusCode;
            ok = response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // transport failure: recorded below, retried like any non-2xx
        }

        db.WebhookDeliveries.Add(
            new WebhookDelivery
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                EndpointId = endpoint.Id,
                EventName = message.EventName,
                Attempt = message.Attempt,
                StatusCode = statusCode,
                Ok = ok,
            }
        );
        await db.SaveChangesAsync(ct);

        if (!ok && message.Attempt < MaxAttempts)
        {
            var baseSeconds = configuration.GetValue("Webhooks:RetryBaseSeconds", 30);
            await bus.ScheduleAsync(
                message with
                {
                    Attempt = message.Attempt + 1,
                },
                TimeSpan.FromSeconds(baseSeconds * Math.Pow(2, message.Attempt - 1)),
                new DeliveryOptions { TenantId = org.Value.ToString() }
            );
        }
    }

    private static string Sign(string secret, long timestamp, string body) =>
        Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes($"{timestamp}.{body}")
            )
        );
}
