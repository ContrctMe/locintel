namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CustodyEventView(
    Guid Id,
    Guid FileId,
    CustodyAction Action,
    Guid ActorId,
    string ActorTier,
    string? Actor,
    string? Detail,
    DateTimeOffset At
);
