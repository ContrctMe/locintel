namespace LocIntel.Modules.Patrols.Routes.Api;

public sealed record RouteMutated(Guid Id, bool Archived, DateTimeOffset UpdatedAt);
