namespace LocIntel.Contracts;

/// <summary>One kind's slice of the audit-trail export: JSONL, newest first.</summary>
public sealed record AuditTrailSection(string Kind, string Jsonl, bool Truncated);
