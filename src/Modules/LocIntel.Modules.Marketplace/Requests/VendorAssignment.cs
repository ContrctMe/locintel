using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The vendor's OWN row for a request it was invited to or assigned (ADR 48):
/// a projection of the requester's snapshot plus this vendor's standing on
/// it. The vendor acts on this row - accept, quote, start, complete - and
/// each action publishes an event the requester applies to the request; the
/// requester's answer (RequestStateChanged) overwrites the snapshot here, so
/// the requester stays authoritative. Keyed (own org, RequestId): a
/// redelivered fan-out lands once. Tier 1.
/// </summary>
public sealed class VendorAssignment : IOrgScoped
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required Guid RequestId { get; init; }
    public required OrgId RequesterOrgId { get; init; }
    public required string RequesterName { get; set; }
    public RequestMode Mode { get; set; }
    public ServiceCategory Category { get; set; }
    public RequestUrgency Urgency { get; set; }
    public RequestStatus Status { get; set; }

    /// <summary>This vendor's standing: invited, quoted, declined, awarded, or passed over.</summary>
    public RecipientStatus Participation { get; set; } = RecipientStatus.Invited;
    public Guid SiteId { get; set; }
    public string SiteName { get; set; } = "";
    public string SiteTimeZone { get; set; } = "Etc/UTC";
    public double? SiteLatitude { get; set; }
    public double? SiteLongitude { get; set; }
    public string? SiteCountryCode { get; set; }
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string SpecJson { get; set; } = "{}";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Rrule { get; set; }
    public decimal? BudgetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ResponseDueAt { get; set; }
    public DateTimeOffset? EscalatedAt { get; set; }
    public int EscalationCount { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? DeclineReason { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionSummary { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? DisputeReason { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether this vendor is THE vendor: chosen directly, or awarded.</summary>
    public bool IsAssigned => Participation == RecipientStatus.Assigned;

    /// <summary>In the vendor's queue: not declined, not passed over.</summary>
    public static bool Open(VendorAssignment a) =>
        a.Participation != RecipientStatus.Declined
        && a.Participation != RecipientStatus.NotSelected;

    /// <summary>The requester's state wins; only this vendor's standing is derived here.</summary>
    public void Apply(RequestSnapshot s)
    {
        RequesterName = s.RequesterName;
        Mode = s.Mode;
        Category = s.Category;
        Urgency = s.Urgency;
        Status = s.Status;
        SiteId = s.SiteId;
        SiteName = s.SiteName;
        SiteTimeZone = s.SiteTimeZone;
        SiteLatitude = s.SiteLatitude;
        SiteLongitude = s.SiteLongitude;
        SiteCountryCode = s.SiteCountryCode;
        Title = s.Title;
        Details = s.Details;
        SpecJson = s.SpecJson;
        StartsAt = s.StartsAt;
        EndsAt = s.EndsAt;
        Rrule = s.Rrule;
        BudgetAmount = s.BudgetAmount;
        Currency = s.Currency;
        SubmittedAt = s.SubmittedAt;
        ResponseDueAt = s.ResponseDueAt;
        EscalatedAt = s.EscalatedAt;
        EscalationCount = s.EscalationCount;
        AcceptedAt = s.AcceptedAt;
        DeclineReason = s.DeclineReason;
        StartedAt = s.StartedAt;
        CompletedAt = s.CompletedAt;
        CompletionSummary = s.CompletionSummary;
        VerifiedAt = s.VerifiedAt;
        DisputeReason = s.DisputeReason;
        CancelledAt = s.CancelledAt;
        CancelReason = s.CancelReason;
        UpdatedAt = s.UpdatedAt;
        if (s.VendorOrgId is { } vendor)
            Participation =
                vendor == OrgId ? RecipientStatus.Assigned : RecipientStatus.NotSelected;
        else if (Participation is RecipientStatus.Assigned or RecipientStatus.NotSelected)
            Participation = RecipientStatus.Invited; // the award was undone (never in v1, but stay honest)
    }
}
