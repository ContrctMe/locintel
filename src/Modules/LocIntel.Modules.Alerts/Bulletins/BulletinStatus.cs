using System.Text.Json.Serialization;

namespace LocIntel.Modules.Alerts.Bulletins;

/// <summary>Lifecycle (tier 1): withdrawn, never deleted; expiry is derived from ExpiresAt.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BulletinStatus
{
    Active,
    Withdrawn,
}
