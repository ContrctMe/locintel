using System.Text.Json.Serialization;

namespace LocIntel.Modules.Cases.Cases;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaseMemberRole
{
    Lead,
    Investigator,
    Reviewer,
}
