using LocIntel.Modules.Marketplace.Marketplace;

namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record RequestMutated(Guid Id, RequestStatus Status, DateTimeOffset UpdatedAt);
