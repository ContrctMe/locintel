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
        AuditRead,
        AuditManage,
        EntitlementsManage,
        RolesManage,
        OrgManage,
        PlatformOperate,
    ];
}
