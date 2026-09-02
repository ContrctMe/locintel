using System.Text.Json.Serialization;

namespace LocIntel.Modules.Marketplace.Marketplace;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialKind
{
    License,
    Insurance,
    Certification,
}
