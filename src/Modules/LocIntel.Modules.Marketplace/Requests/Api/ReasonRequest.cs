namespace LocIntel.Modules.Marketplace.Requests.Api;

/// <summary>Cancel, decline, dispute: the reason lands on the request and the timeline.</summary>
public sealed record ReasonRequest(string Reason);
