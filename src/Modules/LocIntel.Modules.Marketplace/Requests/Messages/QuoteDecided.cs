namespace LocIntel.Modules.Marketplace.Requests.Messages;

/// <summary>The requester awarded (or passed over) a quote; sent to the vendor that owns it.</summary>
public sealed record QuoteDecided(Guid RequestId, Guid QuoteId, bool Accepted, DateTimeOffset At);
