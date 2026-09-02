using System.Text.Json.Serialization;

namespace LocIntel.Modules.Patrols.Patrols;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PatrolStatus
{
    InProgress,
    Completed,
    Abandoned,
}
