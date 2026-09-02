namespace LocIntel.Modules.Patrols.Patrols.Api;

public sealed record PatrolMutated(Guid Id, PatrolStatus Status, DateTimeOffset? EndedAt);
