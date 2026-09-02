using System.Text.Json.Serialization;

namespace LocIntel.Modules.Incidents.Imports;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImportStatus
{
    Staged,
    Committed,
    Discarded,
}
