namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseCustodyResponse(Guid CaseId, IReadOnlyList<CustodyEventView> Events);
