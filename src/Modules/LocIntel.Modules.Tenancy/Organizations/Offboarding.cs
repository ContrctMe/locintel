using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

/// <summary>
/// Tenancy's slice of the offboarding export: the org's own shape - profile,
/// settings, hierarchy, sites, schedules. Windows are derived and excluded.
/// </summary>
public sealed class TenancyExporter(TenancyDbContext db) : IOrgDataExporter
{
    public string Section => "tenancy";

    public async Task<string> ExportJsonAsync(OrgId org, CancellationToken ct = default)
    {
        var organization = await db
            .Organizations.Where(o => o.Id == org)
            .Select(o => new
            {
                o.Name,
                o.Slug,
                region = o.Region.Value,
                o.Status,
                o.CreatedAt,
            })
            .FirstOrDefaultAsync(ct);
        var settings = await db
            .OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrgId == org && s.DeletedAt == null)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);
        var hierarchies = await db
            .Hierarchies.IgnoreQueryFilters()
            .Where(h => h.OrgId == org)
            .Select(h => new
            {
                h.Name,
                h.Levels,
                h.IsAuthoritative,
            })
            .ToListAsync(ct);
        var nodes = await db
            .HierarchyNodes.IgnoreQueryFilters()
            .Where(n => n.OrgId == org)
            .OrderBy(n => n.Path)
            .Select(n => new
            {
                n.Name,
                n.Depth,
                path = n.Path.ToString(),
            })
            .ToListAsync(ct);
        var sites = await db
            .Sites.IgnoreQueryFilters()
            .Where(s => s.OrgId == org)
            .Select(s => new
            {
                s.Name,
                s.TimeZone,
                path = s.Path.ToString(),
                s.ExternalId,
                status = s.Status.ToString(),
                s.AddressLine1,
                s.City,
                s.PostalCode,
                s.CountryCode,
                s.Latitude,
                s.Longitude,
                schedules = db
                    .SiteSchedules.IgnoreQueryFilters()
                    .Where(sch => sch.SiteId == s.Id)
                    .Select(sch => new
                    {
                        sch.Name,
                        sch.RRule,
                        sch.AnchorDate,
                        sch.OpensLocal,
                        sch.ClosesLocal,
                        sch.ExDates,
                    })
                    .ToList(),
            })
            .ToListAsync(ct);
        return JsonSerializer.Serialize(
            new
            {
                organization,
                settings,
                hierarchies,
                nodes,
                sites,
            },
            OffboardingJson.Options
        );
    }
}

public static class OffboardingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}

/// <summary>Envelope-tenanted purge of Tenancy's org rows. The org anchor row itself survives (ADR 25).</summary>
public static class PurgeOrgSitesHandler
{
    [Transactional]
    public static async Task Handle(
        PurgeOrgSites _,
        TenancyDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var org =
            tenant.OrgId
            ?? throw new InvalidOperationException("purge arrived with no tenant on the envelope");
        await db
            .SiteOpenWindows.IgnoreQueryFilters()
            .Where(w => w.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db
            .SiteSchedules.IgnoreQueryFilters()
            .Where(s => s.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db.Sites.IgnoreQueryFilters().Where(s => s.OrgId == org).ExecuteDeleteAsync(ct);
        await db
            .HierarchyNodes.IgnoreQueryFilters()
            .Where(n => n.OrgId == org)
            .ExecuteDeleteAsync(ct);
        await db.Hierarchies.IgnoreQueryFilters().Where(h => h.OrgId == org).ExecuteDeleteAsync(ct);
        await db
            .OrganizationSettings.IgnoreQueryFilters()
            .Where(s => s.OrgId == org)
            .ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// The lifecycle tail (ADR 25): export is self-serve data portability; offboard
/// is operator custody, gated on a prior suspension so it is always a
/// deliberate two-step. Data purges module-by-module; the org anchor row and
/// the audit trail remain.
/// </summary>
public static class LifecycleEndpoints
{
    /// <summary>Self-serve export: any org manager can take the org's data with them.</summary>
    [WolverinePost("/api/org/export")]
    public static async Task<IResult> ExportSelf(
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current
                is not Principal.User { ActiveOrg: { } orgId, UserId: var userId } principal
            || !await scopes.CanAsync(principal, Capabilities.OrgManage, ct)
        )
            return Results.Unauthorized();
        await bus.PublishForOrgAsync(orgId, new ExportOrgData(userId));
        return Results.Accepted(value: new { status = "queued", destination = "files" });
    }

    /// <summary>Operator export: taken before offboarding so nothing leaves undelivered.</summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/operator/orgs/{orgId}/export")]
    public static async Task<IResult> ExportForOrg(
        Guid orgId,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { UserId: var operatorId } principal
            || !await operators.IsOperatorAsync(principal, ct)
        )
            return Results.Unauthorized();
        var target = new OrgId(orgId);
        if (!await db.Organizations.AnyAsync(o => o.Id == target, ct))
            return Results.NotFound();
        await bus.PublishForOrgAsync(target, new ExportOrgData(operatorId));
        return Results.Accepted(value: new { status = "queued", destination = "files" });
    }

    /// <summary>
    /// The point of no return - and still not a row delete. Requires the org
    /// to already be Suspended (enforcement is live, members are locked out),
    /// then purges module data via the fan-out and retires the anchor row to
    /// Offboarding. The audit trail is deliberately left standing.
    /// </summary>
    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/operator/orgs/{orgId}/offboard")]
    public static async Task<IResult> Offboard(
        Guid orgId,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IOperatorContext operators,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (
            accessor.Current is not Principal.User { UserId: var operatorId } principal
            || !await operators.IsOperatorAsync(principal, ct)
        )
            return Results.Unauthorized();
        var target = new OrgId(orgId);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == target, ct);
        if (org is null)
            return Results.NotFound();
        if (org.IsPlatform)
            return Results.BadRequest(new { error = "the platform org cannot be offboarded" });
        if (org.Status == OrganizationStatus.Offboarding)
            return Results.NoContent();
        if (org.Status != OrganizationStatus.Suspended)
            return Results.Conflict(
                new
                {
                    error = "suspend the org first - offboarding is a two-step",
                    code = "not_suspended",
                }
            );

        org.Status = OrganizationStatus.Offboarding;
        await db.SaveChangesAsync(ct);

        // the audit record goes first: purge handlers race it, the trail survives it
        await bus.AuditAsync(org.Id, AuditActor.User(operatorId), "org.offboarded", new { });
        await OrgPurgeFanOut.PublishAsync(bus, target, org.ExternalId);
        return Results.NoContent();
    }
}
