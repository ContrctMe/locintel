namespace LocIntel.Modules.Marketplace.Requests.Api;

public sealed record SubmitQuoteRequest(
    decimal Amount,
    string? Notes = null,
    DateTimeOffset? ValidUntil = null
);
