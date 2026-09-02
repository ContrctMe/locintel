using LocIntel.Modules.Marketplace.Marketplace;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>
/// The two-party row (blueprint: marketplace tenancy). OrgId is the
/// requester, VendorOrgId the fulfiller; RLS and the context's "Tenant"
/// filter admit EITHER org, and each side's endpoints narrow to their own
/// role. Site facts are snapshotted at creation (name, zone, coordinates,
/// ancestor path) because the vendor's tenant context can never read the
/// requester's Tenancy rows. Deletion tier 1: cancelled, never deleted.
/// Temporal: StartsAt/EndsAt and the *At stamps are UTC instants; Rrule is
/// a wall-clock recurring rule in the site's zone (ADR 26/27).
/// </summary>
public sealed class ServiceRequest
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }

    /// <summary>Null while a Broadcast request is out for quotes; set when the buyer awards one.</summary>
    public OrgId? VendorOrgId { get; set; }
    public RequestMode Mode { get; init; } = RequestMode.Direct;
    public required ServiceCategory Category { get; init; }
    public required RequestUrgency Urgency { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Draft;
    public required Guid SiteId { get; init; }
    public required string SiteName { get; init; }
    public required string SiteTimeZone { get; init; }
    public double? SiteLatitude { get; init; }
    public double? SiteLongitude { get; init; }
    public string? SiteCountryCode { get; init; }
    public required LTree Path { get; init; }
    public required string RequesterName { get; init; }
    public required string Title { get; set; }
    public string Details { get; set; } = "";

    /// <summary>Category-specific structured fields (headcount, armed, items...) as a flat JSON object. jsonb.</summary>
    public string SpecJson { get; set; } = "{}";
    public required DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Rrule { get; set; }
    public decimal? BudgetAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public Guid? IncidentId { get; init; }
    public Guid? CaseId { get; init; }
    public required Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>When a response (accept, quote, or decline) is due; the SLA sweep escalates past it. UTC instant.</summary>
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

    public bool IsTerminal =>
        Status is RequestStatus.Declined or RequestStatus.Verified or RequestStatus.Cancelled;
}
