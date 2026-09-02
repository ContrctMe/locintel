using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Tenancy.Organizations;

/// <summary>
/// Tenancy's implementation of the cross-module org read contract.
/// Organizations are platform-global (no tenant filter/RLS), so these lookups
/// work before any tenant context exists - guest resolution depends on that.
/// </summary>
public sealed class OrganizationLookup(TenancyDbContext db) : IOrganizationLookup
{
    public async Task<OrgSummary?> FindBySlugAsync(string slug, CancellationToken ct = default) =>
        Map(await db.Organizations.FirstOrDefaultAsync(o => o.Slug == slug, ct));

    public async Task<OrgSummary?> FindByExternalIdAsync(
        string externalId,
        CancellationToken ct = default
    ) => Map(await db.Organizations.FirstOrDefaultAsync(o => o.ExternalId == externalId, ct));

    public async Task<OrgSummary?> GetAsync(OrgId id, CancellationToken ct = default) =>
        Map(await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct));

    public async Task<IReadOnlyList<OrgId>> ListIdsAsync(CancellationToken ct = default) =>
        await db.Organizations.Select(o => o.Id).ToListAsync(ct);

    private static OrgSummary? Map(Organization? o) =>
        o is null ? null : new OrgSummary(o.Id, o.Name, o.Slug, o.Region, o.ExternalId);
}
