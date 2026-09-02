using System.Text.Json.Serialization;

namespace LocIntel.Modules.Network.Bulletins;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SharedBulletinKind
{
    Bolo,
    Advisory,
}
