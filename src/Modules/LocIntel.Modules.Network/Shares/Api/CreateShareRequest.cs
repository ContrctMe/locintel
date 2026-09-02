namespace LocIntel.Modules.Network.Shares.Api;

public sealed record CreateShareRequest(string Name, string? Description = null);
