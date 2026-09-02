namespace LocIntel.Modules.Network.Shares.Api;

public sealed record ShareListResponse(IReadOnlyList<ShareSummary> Items);
