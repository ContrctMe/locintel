namespace LocIntel.Modules.Cases.Cases.Api;

/// <summary>
/// The prosecution package, v1: everything the case holds as one document
/// - the detail, the custody chain, and the export itself is logged as an
/// Exported custody event on every file. Rendering to PDF is a later step.
/// </summary>
public sealed record CasePackage(
    DateTimeOffset ExportedAt,
    Guid ExportedBy,
    string? ExportedByLabel,
    CaseDetail Case,
    IReadOnlyList<CustodyEventView> Custody
);
