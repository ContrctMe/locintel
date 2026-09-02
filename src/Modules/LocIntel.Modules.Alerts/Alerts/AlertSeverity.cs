using System.Text.Json.Serialization;

namespace LocIntel.Modules.Alerts.Alerts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertSeverity
{
    Low,
    Medium,
    High,
    Critical,
}
