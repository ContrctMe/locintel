using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Sites;

public sealed record CreateAttributeDefinitionRequest(
    string Key,
    string Label,
    string Type,
    bool Public = false
);

public sealed record AttributeDefinitionResponse(
    Guid Id,
    string Key,
    string Label,
    string Type,
    bool Public
);

/// <summary>
/// The org's own site schema (ADR 46): definitions here, values in each
/// site's jsonb. sites:manage owns both - this is site-domain data model,
/// the same custody as the sites themselves.
/// </summary>
public static class SiteAttributeEndpoints
{
    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/sites/attributes")]
    [ProducesResponseType(typeof(List<AttributeDefinitionResponse>), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        // definitions are readable wherever sites are: the console form and
        // the public page both need labels
        if (!await scopes.CanAsync(accessor.Current, Capabilities.SitesRead, ct))
            return new GateOutcome.Forbidden(Capabilities.SitesRead).ToResult();
        return Results.Ok(await DefinitionsAsync(db, ct));
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePost("/api/sites/attributes")]
    [ProducesResponseType(typeof(AttributeDefinitionResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Create(
        CreateAttributeDefinitionRequest request,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.SitesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var key = request.Key.Trim().ToLowerInvariant();
        if (
            key.Length is < 1 or > 60
            || !key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_')
        )
            return ApiErrors.BadRequest(
                "key must be 1-60 chars of lowercase letters, digits, underscores"
            );
        if (string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 100)
            return ApiErrors.BadRequest("label must be 1-100 characters");
        if (!Enum.TryParse<SiteAttributeType>(request.Type, ignoreCase: true, out var type))
            return ApiErrors.BadRequest("type must be Text, Number, or Boolean");
        if (await db.SiteAttributeDefinitions.AnyAsync(d => d.Key == key, ct))
            return ApiErrors.Conflict($"attribute '{key}' already exists");

        var definition = new SiteAttributeDefinition
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Key = key,
            Label = request.Label.Trim(),
            Type = type,
            Public = request.Public,
        };
        db.SiteAttributeDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);
        return Results.Ok(
            new AttributeDefinitionResponse(
                definition.Id,
                definition.Key,
                definition.Label,
                definition.Type.ToString(),
                definition.Public
            )
        );
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverineDelete("/api/sites/attributes/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Delete(
        Guid id,
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.SitesManage, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var definition = await db.SiteAttributeDefinitions.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition is null)
            return Results.NotFound();
        db.SiteAttributeDefinitions.Remove(definition);
        // tier 3, honestly: the values go WITH the definition - an orphaned
        // key in some sites' jsonb would be schema debt nobody can see
        await db.Database.ExecuteSqlAsync(
            $"UPDATE tenancy.sites SET attributes = attributes - {definition.Key}",
            ct
        );
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<List<AttributeDefinitionResponse>> DefinitionsAsync(
        TenancyDbContext db,
        CancellationToken ct
    ) =>
        await db
            .SiteAttributeDefinitions.OrderBy(d => d.Key)
            .Select(d => new AttributeDefinitionResponse(
                d.Id,
                d.Key,
                d.Label,
                d.Type.ToString(),
                d.Public
            ))
            .ToListAsync(ct);

    /// <summary>
    /// Patch-merge incoming values over the stored jsonb, validated against
    /// the org's definitions: unknown keys and wrong types are 400s, null
    /// removes. Returns the error message, or null with the merged JSON.
    /// </summary>
    public static async Task<(string? Error, string? MergedJson)> MergeAttributesAsync(
        TenancyDbContext db,
        string storedJson,
        Dictionary<string, JsonElement> incoming,
        CancellationToken ct
    )
    {
        var definitions = await db.SiteAttributeDefinitions.ToDictionaryAsync(d => d.Key, ct);
        var current = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(storedJson) ?? [];
        foreach (var (key, value) in incoming)
        {
            if (!definitions.TryGetValue(key, out var definition))
                return ($"unknown attribute '{key}' - define it first", null);
            if (value.ValueKind is JsonValueKind.Null)
            {
                current.Remove(key);
                continue;
            }
            var ok = definition.Type switch
            {
                SiteAttributeType.Text => value.ValueKind == JsonValueKind.String,
                SiteAttributeType.Number => value.ValueKind == JsonValueKind.Number,
                SiteAttributeType.Boolean => value.ValueKind
                    is JsonValueKind.True
                        or JsonValueKind.False,
                _ => false,
            };
            if (!ok)
                return ($"attribute '{key}' must be a {definition.Type}", null);
            current[key] = value;
        }
        return (null, JsonSerializer.Serialize(current));
    }
}
