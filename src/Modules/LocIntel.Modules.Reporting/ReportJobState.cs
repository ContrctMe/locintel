using System.Text.Json.Serialization;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// A run's lifecycle. Terminal states start retention; only Queued and Running
/// are claimable by a worker. Wire and column values are the member name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportJobState>))]
public enum ReportJobState
{
    Queued,
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
    Canceled,

    /// <summary>The lease outlived its work and the sweep reclaimed the run.</summary>
    Expired,

    /// <summary>Organization purge is removing this run; nothing may be served.</summary>
    Purging,
}
