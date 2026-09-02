using System.Text.Json.Serialization;

namespace LocIntel.Modules.Entities.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LinkRole
{
    Suspect,
    Victim,
    Witness,
    Associate,
    VehicleUsed,
    Other,
}
