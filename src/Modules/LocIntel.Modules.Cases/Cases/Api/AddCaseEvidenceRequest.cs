namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record AddCaseEvidenceRequest(Guid FileId, string? Label = null);
