namespace LocIntel.Platform.Entitlements;

public enum EntitlementShape
{
    Boolean,
    Limit,
    Tiered,
    Metered,
}

/// <summary>Per-entitlement limit behavior from the closed set (ADR 9).</summary>
public enum LimitPolicy
{
    Block,
    Grace,
    Overage,
    WarnOnly,
}

public sealed record EntitlementDescriptor(
    string Code,
    EntitlementShape Shape,
    LimitPolicy Policy,
    string DefaultValue
)
{
    public long DefaultAsLong => long.Parse(DefaultValue);
}

/// <summary>
/// The entitlement TYPE registry: codes, shapes, and policies are template
/// code (reviewed, typed, codegen'd to TS per ADR 16); per-org VALUES and
/// exceptions are data (ADR 10). Forks extend this list.
/// </summary>
public static class EntitlementCatalog
{
    /// <summary>Levels below the root an org's hierarchy may define (ADR 8's canonical limit).</summary>
    public const string HierarchyDepth = "hierarchy.depth";

    /// <summary>Maximum sites; Block at the ceiling.</summary>
    public const string MaxSites = "sites.max";

    /// <summary>Whether the organization may submit or retry report generation.</summary>
    public const string ReportsEnabled = "reports.enabled";

    /// <summary>PDF outputs reserved or consumed in a UTC calendar month; strict Block admission.</summary>
    public const string ReportsMonthly = "reports.monthly";

    /// <summary>Contact links on/off (boolean gate on the whole feature).</summary>
    public const string ContactLinksEnabled = "contact_links.enabled";

    /// <summary>Contact links issued per month; Grace absorbs the approximate live count.</summary>
    public const string ContactLinksMonthly = "contact_links.monthly";

    /// <summary>Audit retention in days (tiered) - drives the purge job.</summary>
    public const string AuditRetentionDays = "audit.retention_days";

    /// <summary>Whether the plan includes read/access logging at all (the entitlement half of the audit policy).</summary>
    public const string AuditReadLogging = "audit.read_logging";

    /// <summary>Enterprise SSO + directory sync self-service (ADR 41's boolean gate on the admin portal).</summary>
    public const string SsoEnabled = "sso.enabled";

    /// <summary>The fulfillment marketplace, requester side: raising requests to vendors (boolean gate; vendors need no plan).</summary>
    public const string MarketplaceEnabled = "marketplace.enabled";

    /// <summary>Cross-org intelligence sharing (boolean gate on creating shares and publishing into them).</summary>
    public const string NetworkEnabled = "network.enabled";

    /// <summary>AI assistance (classification suggestions, case briefs) - boolean gate.</summary>
    public const string AiAssist = "ai.assist";

    public static readonly IReadOnlyDictionary<string, EntitlementDescriptor> Definitions =
        new Dictionary<string, EntitlementDescriptor>
        {
            [HierarchyDepth] = new(HierarchyDepth, EntitlementShape.Limit, LimitPolicy.Block, "4"),
            [MaxSites] = new(MaxSites, EntitlementShape.Limit, LimitPolicy.Block, "100"),
            [ReportsEnabled] = new(
                ReportsEnabled,
                EntitlementShape.Boolean,
                LimitPolicy.Block,
                "true"
            ),
            [ReportsMonthly] = new(
                ReportsMonthly,
                EntitlementShape.Limit,
                LimitPolicy.Block,
                "1000"
            ),
            [ContactLinksEnabled] = new(
                ContactLinksEnabled,
                EntitlementShape.Boolean,
                LimitPolicy.Block,
                "true"
            ),
            [ContactLinksMonthly] = new(
                ContactLinksMonthly,
                EntitlementShape.Metered,
                LimitPolicy.Grace,
                "1000"
            ),
            [AuditRetentionDays] = new(
                AuditRetentionDays,
                EntitlementShape.Tiered,
                LimitPolicy.WarnOnly,
                "90"
            ),
            [AuditReadLogging] = new(
                AuditReadLogging,
                EntitlementShape.Boolean,
                LimitPolicy.Block,
                "true"
            ),
            [SsoEnabled] = new(SsoEnabled, EntitlementShape.Boolean, LimitPolicy.Block, "false"),
            [MarketplaceEnabled] = new(
                MarketplaceEnabled,
                EntitlementShape.Boolean,
                LimitPolicy.Block,
                "true"
            ),
            [NetworkEnabled] = new(
                NetworkEnabled,
                EntitlementShape.Boolean,
                LimitPolicy.Block,
                "true"
            ),
            [AiAssist] = new(AiAssist, EntitlementShape.Boolean, LimitPolicy.Block, "true"),
        };

    /// <summary>Grace allows this fraction over the ceiling before blocking (ADR 9).</summary>
    public const double GraceFactor = 1.10;
}
