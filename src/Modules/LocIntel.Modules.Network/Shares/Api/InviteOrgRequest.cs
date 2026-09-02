namespace LocIntel.Modules.Network.Shares.Api;

/// <summary>Invite by the org's public slug (the same identity their public site uses).</summary>
public sealed record InviteOrgRequest(string Slug);
