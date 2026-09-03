using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Messages;

/// <summary>Fanned out to each vendor the requester chose (ADR 48 push): the vendor's handler materializes its own assignment row.</summary>
public sealed record RequestOffered(RequestSnapshot Request, RecipientStatus Participation);
