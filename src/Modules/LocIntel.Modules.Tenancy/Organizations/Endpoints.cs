using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

public sealed record SettingResponse(Guid Id, string Key, string Value);

public sealed record PutSettingRequest(string Value);

/// <summary>
/// Reference endpoints for the module pattern. Note what is ABSENT: no
/// .Where(OrgId == ...) anywhere - the Tenant query filter plus RLS scope every
/// query, and the isolation suite proves it. Real authn arrives in step 2
/// (ADR 14); until then the dev-only header principal supplies the org.
/// </summary>
public static class SettingsEndpoints
{
    [WolverineGet("/api/settings")]
    public static async Task<IReadOnlyList<SettingResponse>> List(
        TenancyDbContext db,
        CancellationToken ct
    ) =>
        await db
            .OrganizationSettings.OrderBy(s => s.Key)
            .Select(s => new SettingResponse(s.Id, s.Key, s.Value))
            .ToListAsync(ct);

    [WolverineGet("/api/settings/{id}")]
    public static async Task<IResult> Get(Guid id, TenancyDbContext db, CancellationToken ct)
    {
        var setting = await db
            .OrganizationSettings.Where(s => s.Id == id)
            .Select(s => new SettingResponse(s.Id, s.Key, s.Value))
            .FirstOrDefaultAsync(ct);
        return setting is null ? Results.NotFound() : Results.Ok(setting);
    }

    [WolverinePut("/api/settings/{key}")]
    public static async Task<SettingResponse> Put(
        string key,
        PutSettingRequest request,
        TenancyDbContext db,
        ITenantContext tenant,
        CancellationToken ct
    )
    {
        var setting = await db.OrganizationSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            setting = OrganizationSetting.Create(tenant.OrgId!.Value, key, request.Value);
            db.OrganizationSettings.Add(setting);
        }
        else
        {
            setting.Value = request.Value;
        }
        await db.SaveChangesAsync(ct);
        return new SettingResponse(setting.Id, setting.Key, setting.Value);
    }
}
