using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Identity.Users;

/// <summary>
/// How one member of one org wants to be reached beyond email. Opt-in
/// per kind; the phone is E.164 and unverified in v1 (a fork adds a
/// verification code before trusting it for anything sensitive). Tier 3.
/// UpdatedAt is a UTC instant.
/// </summary>
public sealed class NotificationPreference : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid UserId { get; init; }
    public string? Phone { get; set; }
    public bool SmsAlerts { get; set; }
    public bool SmsMarketplace { get; set; }
    public bool SmsNetwork { get; set; }
    public bool BrowserAlerts { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool WantsSms(string kind) =>
        Phone is not null
        && kind switch
        {
            "alerts" => SmsAlerts,
            "marketplace" => SmsMarketplace,
            "network" => SmsNetwork,
            _ => false,
        };
}
