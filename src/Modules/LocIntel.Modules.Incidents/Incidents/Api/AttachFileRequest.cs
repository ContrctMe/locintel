namespace LocIntel.Modules.Incidents.Incidents.Api;

/// <summary>The file already exists in Storage (ticket flow, ADR 19); this only links it.</summary>
public sealed record AttachFileRequest(Guid FileId, string? Label = null);
