namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record SlaSweepQueued(DateTimeOffset QueuedAt, int Overdue);
