namespace LocIntel.Modules.Network.Shares;

/// <summary>Handled under the INVITEE's tenant: creates their own access row and notifies them.</summary>
public sealed record ShareInvitationRequested(
    Guid ShareId,
    string ShareName,
    string OwnerName,
    Guid InvitedBy
);
