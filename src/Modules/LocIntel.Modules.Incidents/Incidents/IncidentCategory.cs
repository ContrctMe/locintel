using System.Text.Json.Serialization;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// The v1 taxonomy, cut for retail loss prevention (blueprint: v1 segment).
/// Stored as text so a fork can add values without a data migration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IncidentCategory
{
    Theft,
    OrganizedRetailCrime,
    InternalTheft,
    Fraud,
    Robbery,
    Burglary,
    Assault,
    Threat,
    Vandalism,
    Trespass,
    Disturbance,
    Safety,
    Other,
}
