namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CloseCaseRequest(CaseDisposition Disposition, string? Note = null);
