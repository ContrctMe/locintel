namespace LocIntel.Modules.Entities.Entities.Api;

/// <summary>Pending is how many unheld records are past their date right now; the sweep runs asynchronously.</summary>
public sealed record RetentionSweepQueued(DateTimeOffset QueuedAt, int Pending);
