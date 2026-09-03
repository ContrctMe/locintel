using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Network.Shares.Messages;

/// <summary>Handled under the INVITEE's tenant: creates their own access row (with the share's projection) and notifies them.</summary>
public sealed record ShareInvitationRequested(
    Guid ShareId,
    OrgId OwnerOrgId,
    string ShareName,
    string Description,
    string OwnerName,
    ShareStatus ShareStatus,
    string RosterJson,
    Guid InvitedBy
);
