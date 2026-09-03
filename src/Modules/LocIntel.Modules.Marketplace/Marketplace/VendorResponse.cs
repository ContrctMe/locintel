using System.Text.Json.Serialization;

namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>What a vendor did on its own assignment row; the requester's handler applies it to the request.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VendorResponse
{
    Accepted,
    Declined,
    DeclinedToQuote,
    Started,
    Completed,
}
