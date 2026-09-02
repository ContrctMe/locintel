namespace LocIntel.Modules.Marketplace.Marketplace;

/// <summary>
/// Marketplace:* - response windows per urgency and how wide a broadcast
/// goes at first. The SLA sweep escalates (widens a broadcast, nudges the
/// buyer on a direct request) once a window passes with no response.
/// </summary>
public sealed class MarketplaceOptions
{
    public int EmergencyResponseMinutes { get; set; } = 15;
    public int ScheduledResponseMinutes { get; set; } = 24 * 60;
    public int StandingResponseMinutes { get; set; } = 48 * 60;
    public int InitialRecipients { get; set; } = 10;
    public int EscalationRecipients { get; set; } = 10;
    public int MaxEscalations { get; set; } = 3;

    public TimeSpan Window(RequestUrgency urgency) =>
        TimeSpan.FromMinutes(
            urgency switch
            {
                RequestUrgency.Emergency => EmergencyResponseMinutes,
                RequestUrgency.Standing => StandingResponseMinutes,
                _ => ScheduledResponseMinutes,
            }
        );
}
