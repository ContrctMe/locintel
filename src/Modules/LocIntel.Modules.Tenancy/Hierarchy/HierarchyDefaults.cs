namespace LocIntel.Modules.Tenancy.Hierarchy;

/// <summary>
/// The hierarchy every org is born with (flow review, 2026-09): a root named
/// after the org and these level names, so the first site has somewhere to
/// sit without a provisioning step. Owners rename the levels on the
/// Hierarchy page; the names are per-org data, this is only the first draft.
/// </summary>
public static class HierarchyDefaults
{
    public static readonly string[] Levels = ["Region", "Market"];
}
