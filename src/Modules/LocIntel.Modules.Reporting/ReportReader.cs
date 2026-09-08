using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// One principal's read authority over reports, resolved once per request.
/// Reading a run needs file-read and site-read together, so <see cref="Scope"/>
/// is their intersection and is the only scope a read path may use.
/// Resolving this per job turned a run listing into a query storm.
/// </summary>
public sealed record ReportReader(Principal.User User, NodeScope Scope);
