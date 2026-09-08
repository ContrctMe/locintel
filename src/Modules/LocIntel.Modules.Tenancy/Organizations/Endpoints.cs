using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

public sealed record SettingResponse(Guid Id, string Key, string Value);

public sealed record PutSettingRequest(string Value);

/// <summary>Private organization settings. Typed settings own reserved namespaces.</summary>
public static class SettingsEndpoints
{
    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/settings")]
    [ProducesResponseType(typeof(IReadOnlyList<SettingResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        return Results.Ok(
            await db
                .OrganizationSettings.OrderBy(s => s.Key)
                .Select(s => new SettingResponse(s.Id, s.Key, s.Value))
                .ToListAsync(ct)
        );
    }

    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/settings/{id}")]
    [ProducesResponseType(typeof(SettingResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var setting = await db
            .OrganizationSettings.Where(s => s.Id == id)
            .Select(s => new SettingResponse(s.Id, s.Key, s.Value))
            .FirstOrDefaultAsync(ct);
        return setting is null ? Results.NotFound() : Results.Ok(setting);
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePut("/api/settings/{key}")]
    [ProducesResponseType(typeof(SettingResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Put(
        string key,
        PutSettingRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Org: var org })
            return gate.ToResult();
        if (
            key.Length is 0 or > 200
            || key.StartsWith("map.", StringComparison.OrdinalIgnoreCase)
            || request.Value is null
            || request.Value.Length > 65536
        )
            return ApiErrors.BadRequest(
                "invalid or reserved setting; use the typed settings endpoint"
            );
        var setting = await db.OrganizationSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            setting = OrganizationSetting.Create(org, key, request.Value);
            db.OrganizationSettings.Add(setting);
        }
        else
            setting.Value = request.Value;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new SettingResponse(setting.Id, setting.Key, setting.Value));
    }
}
