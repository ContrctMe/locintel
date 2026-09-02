namespace LocIntel.Modules.Entities.Entities.Api;

public sealed record GrantEntityAccessRequest(Guid UserId, string Reason, DateTimeOffset ExpiresAt);
