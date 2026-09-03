namespace LocIntel.Modules.Marketplace.Requests.Messages;

/// <summary>Fanned out to every participant after the requester changes its request; each vendor's row applies the snapshot whole.</summary>
public sealed record RequestStateChanged(RequestSnapshot Request);
