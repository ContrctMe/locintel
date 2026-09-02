using System.Text.Json.Serialization;

namespace LocIntel.Modules.Entities.Entities;

/// <summary>
/// A record about a real person starts as an allegation. Confirmed requires
/// evidence (at least one linked incident); Cleared keeps the record for the
/// retention window so the same person is not re-suspected from scratch.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntityStatus
{
    Suspected,
    Confirmed,
    Cleared,
}
