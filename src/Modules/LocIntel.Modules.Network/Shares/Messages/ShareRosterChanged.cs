namespace LocIntel.Modules.Network.Shares.Messages;

/// <summary>Fanned out by the owner to every member: the share's projection and roster as they now stand.</summary>
public sealed record ShareRosterChanged(
    Guid ShareId,
    string ShareName,
    string Description,
    string OwnerName,
    ShareStatus ShareStatus,
    string RosterJson
);
