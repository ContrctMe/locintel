namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseListResponse(IReadOnlyList<CaseSummary> Items, int Total, int? NextOffset);
