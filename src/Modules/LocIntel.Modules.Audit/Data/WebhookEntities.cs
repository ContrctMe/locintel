using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Audit.Data;

/// <summary>
/// An outbound webhook subscription (ADR 40): the org's own event record,
/// pushed. Lives in the audit module because the domain-event stream IS what
/// gets delivered. The signing secret is envelope-encrypted (ADR 31), shown
/// once at creation. Deletion tier 3: configuration, hard-deleted.
/// </summary>
public sealed class WebhookEndpoint : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Url { get; init; }
    public required byte[] EncryptedSecret { get; set; }

    /// <summary>Event names to deliver; "prefix.*" wildcards allowed; empty = everything.</summary>
    public string[] Events { get; set; } = [];

    public bool Active { get; set; } = true;

    /// <summary>
    /// Rotation's dual-secret window: deliveries are signed with BOTH secrets
    /// until the previous one expires, so the consumer swaps at their own
    /// pace with zero rejected deliveries.
    /// </summary>
    public byte[]? PreviousEncryptedSecret { get; set; }

    /// <summary>UTC instant (ADR 26): when the previous secret stops signing.</summary>
    public DateTimeOffset? PreviousSecretExpiresAt { get; set; }

    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool Matches(string eventName) =>
        Events.Length == 0
        || Events.Any(e => e == eventName || (e.EndsWith(".*") && eventName.StartsWith(e[..^1])));
}

/// <summary>One delivery attempt's outcome - the tenant-visible debugging trail. Purged by audit retention.</summary>
public sealed class WebhookDelivery : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid EndpointId { get; init; }
    public required string EventName { get; init; }
    public required int Attempt { get; init; }
    public int? StatusCode { get; init; }
    public required bool Ok { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
