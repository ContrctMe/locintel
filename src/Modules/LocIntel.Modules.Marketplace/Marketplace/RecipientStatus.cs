using System.Text.Json.Serialization;

namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>A vendor's standing on a request: invited to quote, quoted, declined, awarded, or passed over.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RecipientStatus
{
    Invited,
    Quoted,
    Declined,
    Assigned,
    NotSelected,
}
