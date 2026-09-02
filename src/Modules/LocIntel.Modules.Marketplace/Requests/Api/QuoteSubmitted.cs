using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record QuoteSubmitted(Guid Id, QuoteStatus Status);
