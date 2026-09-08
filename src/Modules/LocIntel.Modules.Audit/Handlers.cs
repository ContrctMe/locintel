using LocIntel.Contracts;
using LocIntel.Modules.Audit.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Audit;

/// <summary>
/// The async halves of ADR 13: domain/authz entries arrive via the durable
/// outbox (transactional with their cause when published inside one); access
/// entries ride the same durable queue but are pure fire-and-forget volume.
/// Tenant rides the envelope (ADR 24) and is read lazily.
/// </summary>
public static class RecordDomainAuditHandler
{
    [Transactional]
    public static async Task Handle(
        RecordDomainAudit message,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        Wolverine.IMessageBus bus,
        CancellationToken ct
    )
    {
        var org = RequireOrg(tenant, envelope);
        db.DomainEvents.Add(
            new DomainLogEntry
            {
                Id = Guid.CreateVersion7(),
                OrgId = org,
                ActorTier = envelope.Headers.GetValueOrDefault(AuditHeaders.Tier) ?? "system",
                ActorId = ParseActor(envelope),
                EventName = message.EventName,
                Payload = message.PayloadJson,
                OccurredAt = envelope.SentAt,
            }
        );
        await db.SaveChangesAsync(ct);

        // outbound webhooks ride the same stream (ADR 40): the event record
        // and its subscriptions live in this module, so the fan-out is one
        // same-context query away
        var endpoints = await db
            .WebhookEndpoints.Where(e => e.Active)
            .OrderBy(e => e.CreatedAt)
            .Take(WebhookDispatch.MaxEndpoints)
            .ToListAsync(ct);
        var groupId = Guid.CreateVersion7();
        foreach (var endpoint in endpoints.Where(e => e.Matches(message.EventName)))
            if (
                !await WebhookDispatch.EnqueueAsync(
                    db,
                    new OrgId(org),
                    new DeliverWebhook(
                        endpoint.Id,
                        groupId,
                        message.EventName,
                        message.PayloadJson,
                        envelope.SentAt,
                        Attempt: 1
                    ),
                    bus,
                    ct
                )
            )
                throw new CapacityBusyException();
    }

    private static Guid RequireOrg(ITenantContext tenant, Envelope envelope) =>
        tenant.OrgId?.Value
        ?? throw new InvalidOperationException(
            $"audit message arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
        );

    private static Guid? ParseActor(Envelope envelope) =>
        Guid.TryParse(envelope.Headers.GetValueOrDefault(AuditHeaders.ActorId), out var id)
            ? id
            : null;
}

public static class RecordAuthzAuditHandler
{
    [Transactional]
    public static async Task Handle(
        RecordAuthzAudit message,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        CancellationToken ct
    )
    {
        db.AuthzDecisions.Add(
            new AuthzLogEntry
            {
                Id = Guid.CreateVersion7(),
                OrgId = RequireOrg(tenant, envelope),
                ActorTier = envelope.Headers.GetValueOrDefault(AuditHeaders.Tier) ?? "system",
                ActorId = ParseActor(envelope),
                Action = message.Action,
                Outcome = message.Outcome,
                ScopeSummary = message.ScopeSummary,
                OccurredAt = envelope.SentAt,
            }
        );
        await db.SaveChangesAsync(ct);
    }

    private static Guid RequireOrg(ITenantContext tenant, Envelope envelope) =>
        tenant.OrgId?.Value
        ?? throw new InvalidOperationException(
            $"audit message arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
        );

    private static Guid? ParseActor(Envelope envelope) =>
        Guid.TryParse(envelope.Headers.GetValueOrDefault(AuditHeaders.ActorId), out var id)
            ? id
            : null;
}

public static class RecordAccessAuditHandler
{
    [Transactional]
    public static async Task Handle(
        RecordAccessAudit message,
        Envelope envelope,
        ITenantContext tenant,
        AuditDbContext db,
        CancellationToken ct
    )
    {
        db.Accesses.Add(
            new AccessLogEntry
            {
                Id = Guid.CreateVersion7(),
                OrgId = RequireOrg(tenant, envelope),
                ActorTier = envelope.Headers.GetValueOrDefault(AuditHeaders.Tier) ?? "system",
                ActorId = ParseActor(envelope),
                Method = message.Method,
                Path = message.Path,
                StatusCode = message.StatusCode,
                OccurredAt = envelope.SentAt,
            }
        );
        await db.SaveChangesAsync(ct);
    }

    private static Guid RequireOrg(ITenantContext tenant, Envelope envelope) =>
        tenant.OrgId?.Value
        ?? throw new InvalidOperationException(
            $"audit message arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
        );

    private static Guid? ParseActor(Envelope envelope) =>
        Guid.TryParse(envelope.Headers.GetValueOrDefault(AuditHeaders.ActorId), out var id)
            ? id
            : null;
}
