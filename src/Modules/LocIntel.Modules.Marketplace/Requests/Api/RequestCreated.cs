using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record RequestCreated(Guid Id, RequestStatus Status);
