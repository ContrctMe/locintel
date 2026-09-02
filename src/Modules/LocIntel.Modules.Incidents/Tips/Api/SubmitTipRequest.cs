using LocIntel.Modules.Incidents.Incidents;

namespace LocIntel.Modules.Incidents.Tips.Api;

/// <summary>
/// An anonymous tip from the org's public site. Website is a honeypot:
/// humans never see the field, bots fill it. Contact is optional and
/// never shown publicly.
/// </summary>
public sealed record SubmitTipRequest(
    Guid SiteId,
    IncidentCategory Category,
    string Description,
    DateTimeOffset? OccurredAt = null,
    string? Contact = null,
    string? Website = null
);
