namespace LocIntel.Modules.Marketplace.Requests;

/// <summary>Per-org SLA sweep message (ADR 24 fan-out): the tenant rides the envelope.</summary>
public sealed record EscalateOverdueRequests;
