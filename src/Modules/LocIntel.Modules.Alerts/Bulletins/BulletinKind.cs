using System.Text.Json.Serialization;

namespace LocIntel.Modules.Alerts.Bulletins;

/// <summary>BOLO = be on the lookout (a person/vehicle); Advisory = a pattern or MO; Safety = a hazard.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BulletinKind
{
    Bolo,
    Advisory,
    Safety,
}
