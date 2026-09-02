using LocIntel.Platform.Audit;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Tenancy.Organizations;

/// <summary>
/// Org-scoped key/value setting. Deletion tier 2 (ADR 25): soft delete with
/// restore. This is the reference org-scoped entity: IOrgScoped drives the
/// Tenant query filter, and its table carries the RLS policy - the isolation
/// golden suite exercises both.
/// </summary>
public sealed class OrganizationSetting : IOrgScoped, ISoftDeletable
{
    public required Guid Id { get; init; }
    public required OrgId OrgId { get; init; }
    public required string Key { get; init; }

    [AuditRedacted]
    public required string Value { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public static OrganizationSetting Create(OrgId orgId, string key, string value) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrgId = orgId,
            Key = key,
            Value = value,
        };
}
