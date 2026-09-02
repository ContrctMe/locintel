using System.Text.Json.Serialization;

namespace LocIntel.Modules.Entities.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntityKind
{
    Person,
    Vehicle,
    Group,
}
