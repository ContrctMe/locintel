using System.Text.Json.Serialization;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// One PDF's lifecycle inside a run. Succeeded consumes the item's reserved quota
/// unit; Failed and Canceled release it. Wire and column values are the member name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportItemState>))]
public enum ReportItemState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled,
}
