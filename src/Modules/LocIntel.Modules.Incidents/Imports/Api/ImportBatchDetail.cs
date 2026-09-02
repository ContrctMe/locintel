namespace LocIntel.Modules.Incidents.Imports.Api;

public sealed record ImportBatchDetail(ImportBatchView Batch, IReadOnlyList<ImportRowView> Rows);
