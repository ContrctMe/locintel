using System.Text.Json.Serialization;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>How a case ended - the field prosecution statistics and benchmarks group on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaseDisposition
{
    Unfounded,
    Resolved,
    ReferredToPolice,
    Prosecuted,
    Other,
}
