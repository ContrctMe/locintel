using LocIntel.Platform.Entitlements;

namespace LocIntel.Platform.Billing;

/// <summary>A purchasable plan: an entitlement BUNDLE with a price tag.</summary>
public sealed record Plan(
    string Id,
    string Name,
    decimal MonthlyPriceUsd,
    IReadOnlyDictionary<string, string> Entitlements
);

/// <summary>
/// The template's plan ladder (ADR 39). The FREE tier is the entitlement
/// catalog's defaults - an org with no subscription simply has no plan rows
/// and falls through to them. Paid plans only ever RAISE values; forks edit
/// these numbers, never the mechanism. Every code here must exist in
/// EntitlementCatalog.Definitions (a unit test holds that).
/// </summary>
public static class PlanCatalog
{
    public static readonly IReadOnlyList<Plan> Plans =
    [
        new(
            "growth",
            "Growth",
            49m,
            new Dictionary<string, string>
            {
                [EntitlementCatalog.MaxSites] = "500",
                [EntitlementCatalog.ReportsMonthly] = "5000",
                [EntitlementCatalog.HierarchyDepth] = "6",
                [EntitlementCatalog.ContactLinksMonthly] = "10000",
                [EntitlementCatalog.AuditRetentionDays] = "365",
            }
        ),
        new(
            "scale",
            "Scale",
            199m,
            new Dictionary<string, string>
            {
                [EntitlementCatalog.MaxSites] = "5000",
                [EntitlementCatalog.ReportsMonthly] = "50000",
                [EntitlementCatalog.HierarchyDepth] = "8",
                [EntitlementCatalog.ContactLinksMonthly] = "100000",
                [EntitlementCatalog.AuditRetentionDays] = "730",
                [EntitlementCatalog.SsoEnabled] = "true",
            }
        ),
    ];

    public static Plan? Find(string planId) => Plans.FirstOrDefault(p => p.Id == planId);
}
