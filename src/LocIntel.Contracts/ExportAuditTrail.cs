namespace LocIntel.Contracts;

/// <summary>Assemble the org's audit-trail archive (handled by Storage; tenant on the envelope).</summary>
public sealed record ExportAuditTrail(Guid RequestedBy, Guid? AdmissionId = null);
