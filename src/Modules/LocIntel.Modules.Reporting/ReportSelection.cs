using System.Text.Json.Serialization;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// How the caller chose the sites, captured at submission and never widened
/// afterwards (ADR 56). Wire and column values are the lowercase member name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReportSelection>))]
public enum ReportSelection
{
    /// <summary>Exactly the site IDs the request carried.</summary>
    [JsonStringEnumMemberName("selected")]
    Selected,

    /// <summary>Every site inside the requester's scope.</summary>
    [JsonStringEnumMemberName("accessible")]
    Accessible,

    /// <summary>Every site in the organization; requires organization-wide scope.</summary>
    [JsonStringEnumMemberName("organization")]
    Organization,
}
