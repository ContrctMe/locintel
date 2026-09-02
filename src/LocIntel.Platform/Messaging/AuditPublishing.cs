using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Messaging;

/// <summary>
/// Who a domain audit record is attributed to. The tier and id ride the
/// envelope as headers (ADR 13/24) - never inside the message body, where
/// they could be forged by a replay.
/// </summary>
public readonly record struct AuditActor(string Tier, Guid? Id)
{
    /// <summary>A signed-in person.</summary>
    public static AuditActor User(Guid userId) => new("user", userId);

    /// <summary>An API key acting for an org (ADR 40): its own tier, its own id.</summary>
    public static AuditActor Service(Guid keyId) => new("service", keyId);

    /// <summary>The platform itself - a sweep, a webhook, a scheduled job.</summary>
    public static AuditActor System { get; } = new("system", null);
}
