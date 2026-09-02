namespace LocIntel.Platform.Audit;

/// <summary>
/// The change-diff sink row (ADR 12/13). Mapped into EVERY module's DbContext
/// against the audit module's table (excluded from their migrations) so diffs
/// commit in the SAME transaction as the change - the register's consequence
/// of one-database-many-schemas, realized. The audit module owns the table
/// and its migration; everyone else only appends.
/// </summary>
public sealed class AuditChangeLog
{
    public required Guid Id { get; init; }
    public Guid? OrgId { get; init; }
    public required string ActorTier { get; init; }
    public Guid? ActorId { get; init; }
    public string? ActorLabel { get; init; }
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public required string RowId { get; init; }
    public required string Operation { get; init; }
    public required string Diff { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}

/// <summary>
/// Field-level redaction (ADR 12): the diff records that the column changed,
/// never the values. Put this on anything secret- or PII-bearing.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditRedactedAttribute : Attribute;
