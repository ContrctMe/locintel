using System.Text.Json.Serialization;

namespace LocIntel.Modules.Network.Shares;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemberRole
{
    Owner,
    Member,
}
