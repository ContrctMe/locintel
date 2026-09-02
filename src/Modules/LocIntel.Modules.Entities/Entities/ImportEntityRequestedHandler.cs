using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Contracts.Entities;
using LocIntel.Modules.Entities.Data;
using LocIntel.Platform.Kernel;
using Wolverine;
using Wolverine.Attributes;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// Creates a local record from a shared bulletin (tenant from the envelope).
/// Suspected, default retention, no links: the receiving org confirms it
/// with its own evidence like any other record.
/// </summary>
public static class ImportEntityRequestedHandler
{
    [Transactional(typeof(EntitiesDbContext))]
    public static async Task Handle(
        ImportEntityRequested message,
        Envelope envelope,
        ITenantContext tenant,
        EntitiesDbContext db,
        IMessageBus bus,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (tenant.OrgId is not { } org)
            throw new InvalidOperationException(
                $"ImportEntityRequested arrived with no tenant on the envelope (TenantId='{envelope.TenantId}')"
            );
        if (!Enum.TryParse<EntityKind>(message.Kind, out var kind))
            kind = EntityKind.Person;
        var now = time.GetUtcNow();
        var entity = new Entity
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Kind = kind,
            DisplayName = message.DisplayName,
            Aliases = message.Aliases,
            DescriptorsJson = JsonSerializer.Serialize(message.Descriptors),
            Summary = message.Summary,
            ExpiresAt = EntityRetention.Default(now),
            CreatedBy = message.RequestedBy,
        };
        db.Entities.Add(entity);
        await db.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new RecordDomainAudit(
                "entity.imported",
                JsonSerializer.Serialize(new { entity.Id, Kind = entity.Kind.ToString() })
            ),
            new DeliveryOptions
            {
                TenantId = org.Value.ToString(),
                Headers =
                {
                    ["locintel-actor-tier"] = "user",
                    ["locintel-actor-id"] = message.RequestedBy.ToString(),
                },
            }
        );
    }
}
