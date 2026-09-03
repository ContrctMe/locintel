namespace LocIntel.Modules.Network.Shares.Messages;

/// <summary>Handled under the removed member's tenant: marks their own access row Removed and drops their copies.</summary>
public sealed record ShareAccessRevoked(Guid ShareId);
