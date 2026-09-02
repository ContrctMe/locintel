namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record AddCaseEntityRequest(Guid EntityId, string? Note = null);
