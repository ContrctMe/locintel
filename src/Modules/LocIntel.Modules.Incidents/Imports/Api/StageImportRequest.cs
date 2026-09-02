namespace LocIntel.Modules.Incidents.Imports.Api;

/// <summary>A Clean CSV already in Storage. Columns: site, occurred_at, category, severity, title, narrative, loss_amount, police_report, tags.</summary>
public sealed record StageImportRequest(Guid FileId);
