using System.Text.Json.Serialization;

namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>What tenants send out for fulfillment (blueprint: marketplace catalog).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ServiceCategory
{
    GuardService,
    MobilePatrol,
    AlarmResponse,
    Investigation,
    CctvInstall,
    AccessControl,
    BoardUp,
    Restoration,
    LegalSupport,
    EquipmentSupply,
    KeyHolding,
    Other,
}
