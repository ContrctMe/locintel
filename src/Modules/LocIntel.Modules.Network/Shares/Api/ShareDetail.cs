namespace LocIntel.Modules.Network.Shares.Api;

public sealed record ShareDetail(ShareSummary Share, IReadOnlyList<MemberView> Members);
