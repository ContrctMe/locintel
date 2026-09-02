namespace LocIntel.Platform.Kernel;

/// <summary>
/// The permission-action catalog (ADR 16): every (domain, action) string the
/// scope resolver evaluates, as constants - codegen emits these as a TS union
/// so capability strings cannot drift between the two languages. Endpoints
/// reference these, never inline strings.
/// </summary>
public static class Capabilities
{
    /// <summary>The guest tier's reach (ADR 7): public site info for the host-derived org.</summary>
    public const string PublicRead = "public:read";

    public const string SitesRead = "sites:read";
    public const string SitesManage = "sites:manage";
    public const string HierarchyManage = "hierarchy:manage";
    public const string FilesRead = "files:read";
    public const string FilesManage = "files:manage";
    public const string IngestManage = "ingest:manage";
    public const string ChecklistsManage = "checklists:manage";
    public const string ChecklistsComplete = "checklists:complete";

    /// <summary>Incidents (the crime-intelligence fact table): see, file, and manage.</summary>
    public const string IncidentsRead = "incidents:read";
    public const string IncidentsReport = "incidents:report";
    public const string IncidentsManage = "incidents:manage";

    /// <summary>
    /// Persons of interest, vehicles, groups. entities:read is need-to-know:
    /// it reaches only entities linked to incidents within the holder's
    /// scope, or explicitly granted; entities:manage sees and edits all.
    /// </summary>
    public const string EntitiesRead = "entities:read";
    public const string EntitiesManage = "entities:manage";

    /// <summary>Investigations: cases:read reaches cases you are on or whose incidents are in scope; cases:manage runs them.</summary>
    public const string CasesRead = "cases:read";
    public const string CasesManage = "cases:manage";

    /// <summary>
    /// Marketplace, requester side: read requests in scope; manage raises,
    /// submits, cancels, verifies, disputes, and curates preferred vendors.
    /// </summary>
    public const string MarketplaceRead = "marketplace:read";
    public const string MarketplaceManage = "marketplace:manage";

    /// <summary>
    /// Marketplace, vendor side (held inside a VENDOR org): manage the
    /// profile and credentials; fulfill works the incoming requests.
    /// </summary>
    public const string VendorManage = "vendor:manage";
    public const string VendorFulfill = "vendor:fulfill";

    /// <summary>Alerts feed and bulletins (BOLOs): read/acknowledge within scope; manage issues and withdraws.</summary>
    public const string AlertsRead = "alerts:read";
    public const string AlertsManage = "alerts:manage";

    /// <summary>Intelligence network (cross-org sharing): read what your org's shares carry; manage creates, invites, publishes, imports.</summary>
    public const string NetworkRead = "network:read";
    public const string NetworkManage = "network:manage";

    /// <summary>Patrols and guard ops: read reports; perform (start, scan, end) within scope; manage routes and schedules.</summary>
    public const string PatrolsRead = "patrols:read";
    public const string PatrolsPerform = "patrols:perform";
    public const string PatrolsManage = "patrols:manage";
    public const string AuditRead = "audit:read";
    public const string AuditManage = "audit:manage";
    public const string EntitlementsManage = "entitlements:manage";
    public const string RolesManage = "roles:manage";

    /// <summary>Org settings: rename, and (later) offboarding.</summary>
    public const string OrgManage = "org:manage";

    /// <summary>Platform-operator reach: held only inside the flagged platform org.</summary>
    public const string PlatformOperate = "platform:operate";

    public static readonly IReadOnlyList<string> All =
    [
        PublicRead,
        SitesRead,
        SitesManage,
        HierarchyManage,
        FilesRead,
        FilesManage,
        IngestManage,
        ChecklistsManage,
        ChecklistsComplete,
        IncidentsRead,
        IncidentsReport,
        IncidentsManage,
        EntitiesRead,
        EntitiesManage,
        CasesRead,
        CasesManage,
        MarketplaceRead,
        MarketplaceManage,
        VendorManage,
        VendorFulfill,
        AlertsRead,
        AlertsManage,
        NetworkRead,
        NetworkManage,
        PatrolsRead,
        PatrolsPerform,
        PatrolsManage,
        AuditRead,
        AuditManage,
        EntitlementsManage,
        RolesManage,
        OrgManage,
        PlatformOperate,
    ];
}
