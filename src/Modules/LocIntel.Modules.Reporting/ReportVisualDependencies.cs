namespace LocIntel.Modules.Reporting;

/// <summary>Only the protected visuals actually embedded in a completed PDF.</summary>
public sealed record ReportVisualDependencies(Guid[] PhotoIds, Guid[] OverlayIds);
