namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record LinkIncidentRequest(Guid IncidentId, LinkRole Role, string? Note = null);
