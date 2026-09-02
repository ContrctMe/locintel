namespace LocIntel.Modules.Incidents.Incidents.Api;

/// <summary>A note or attachment link is gone; the id echoes back for the client's cache.</summary>
public sealed record IncidentChildRemoved(Guid Id);
