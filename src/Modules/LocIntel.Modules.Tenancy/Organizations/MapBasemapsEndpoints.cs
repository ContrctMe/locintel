using System.Text.Json;
using System.Text.RegularExpressions;
using LocIntel.Contracts;
using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using LocIntel.Platform.Secrets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

/// <summary>One entry as the org edits it; <c>Key</c> is write-only and may be omitted to keep the stored one.</summary>
public sealed record BasemapInput(
    string Id,
    string Name,
    string UrlTemplate,
    string Attribution,
    int? MaxZoom = null,
    string? Key = null
);

public sealed record PutBasemapsRequest(IReadOnlyList<BasemapInput> Basemaps);

/// <summary>What the settings page sees: never the key, only that there is one.</summary>
public sealed record BasemapSetting(
    string Id,
    string Name,
    string UrlTemplate,
    string Attribution,
    int MaxZoom,
    bool HasKey
);

public sealed record BasemapSettingsResponse(IReadOnlyList<BasemapSetting> Basemaps);

/// <summary>What the map draws from: the template with the key in place, as the provider wants it.</summary>
public sealed record Basemap(
    string Id,
    string Name,
    string UrlTemplate,
    string Attribution,
    int MaxZoom
);

public sealed record BasemapListResponse(IReadOnlyList<Basemap> Basemaps);

/// <summary>
/// The org setting <c>map.basemaps</c> (ADR 50 §3): an ordered list of raster
/// basemaps the console map offers beside the open ones it ships with. A
/// provider key is held under the envelope-encryption seam (ADR 31) and
/// substituted into the tile URL when the map asks - the browser receives it
/// in the URL, which is how every tile provider keys by referrer, so this is
/// a convenience for the org, not a secrecy boundary. Paid providers stay
/// fork territory (ADR 43); the shape is here so a fork adds an entry, not a
/// feature.
/// </summary>
public static partial class MapBasemapsEndpoints
{
    public const string SettingKey = "map.basemaps";
    public const int MaxEntries = 10;
    private const string KeyPlaceholder = "{key}";

    /// <summary>The stored shape: the cipher rides along, never the key.</summary>
    private sealed record Stored(
        string Id,
        string Name,
        string UrlTemplate,
        string Attribution,
        int MaxZoom,
        byte[]? KeyCipher
    );

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex IdShape();

    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/map/basemaps")]
    [ProducesResponseType(typeof(BasemapListResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> List(
        TenancyDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.SitesRead, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var basemaps = new List<Basemap>();
        foreach (var entry in await ReadAsync(db, ct))
        {
            var url = entry.UrlTemplate;
            if (entry.KeyCipher is { } cipher)
                url = url.Replace(
                    KeyPlaceholder,
                    Uri.EscapeDataString(await EnvelopeCrypto.DecryptAsync(cipher, kms, ct))
                );
            basemaps.Add(new Basemap(entry.Id, entry.Name, url, entry.Attribution, entry.MaxZoom));
        }
        return Results.Ok(new BasemapListResponse(basemaps));
    }

    [Transactional(
        typeof(TenancyDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/map/basemaps/settings")]
    [ProducesResponseType(typeof(BasemapSettingsResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Settings(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        return Results.Ok(ToSettings(await ReadAsync(db, ct)));
    }

    [Transactional(typeof(TenancyDbContext))]
    [WolverinePut("/api/map/basemaps")]
    [ProducesResponseType(typeof(BasemapSettingsResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Put(
        PutBasemapsRequest request,
        TenancyDbContext db,
        IKeyWrapper kms,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.OrgManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user, Org: var org })
            return gate.ToResult();
        if (request.Basemaps.Count > MaxEntries)
            return ApiErrors.BadRequest($"at most {MaxEntries} basemaps");

        var current = (await ReadAsync(db, ct)).ToDictionary(e => e.Id);
        var next = new List<Stored>();
        foreach (var input in request.Basemaps)
        {
            var id = input.Id?.Trim() ?? "";
            var name = input.Name?.Trim() ?? "";
            var template = input.UrlTemplate?.Trim() ?? "";
            var attribution = input.Attribution?.Trim() ?? "";
            var maxZoom = input.MaxZoom ?? 19;
            if (!IdShape().IsMatch(id))
                return ApiErrors.BadRequest("a basemap id is lowercase letters, digits and dashes");
            if (next.Any(e => e.Id == id))
                return ApiErrors.BadRequest($"basemap '{id}' appears twice");
            if (name.Length is 0 or > 80)
                return ApiErrors.BadRequest($"basemap '{id}' needs a name");
            if (attribution.Length > 300)
                return ApiErrors.BadRequest($"basemap '{id}': attribution is too long");
            if (maxZoom is < 1 or > 22)
                return ApiErrors.BadRequest($"basemap '{id}': maxZoom is 1 to 22");
            if (
                !Uri.TryCreate(template, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !template.Contains("{z}")
                || !template.Contains("{x}")
                || !template.Contains("{y}")
            )
                return ApiErrors.BadRequest(
                    $"basemap '{id}': the URL template must be https with {{z}}, {{x}} and {{y}}"
                );

            byte[]? cipher = null;
            if (template.Contains(KeyPlaceholder))
            {
                // a new key replaces; none given keeps the one stored under the same id
                if (!string.IsNullOrWhiteSpace(input.Key))
                    cipher = await EnvelopeCrypto.EncryptAsync(input.Key.Trim(), kms, ct);
                else if (current.TryGetValue(id, out var existing))
                    cipher = existing.KeyCipher;
                if (cipher is null)
                    return ApiErrors.BadRequest(
                        $"basemap '{id}': the template names {{key}} but no key was given"
                    );
            }
            next.Add(new Stored(id, name, template, attribution, maxZoom, cipher));
        }

        var setting = await db.OrganizationSettings.FirstOrDefaultAsync(
            s => s.Key == SettingKey,
            ct
        );
        var value = JsonSerializer.Serialize(next, Json);
        if (setting is null)
            db.OrganizationSettings.Add(OrganizationSetting.Create(org, SettingKey, value));
        else
            setting.Value = value;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(user.UserId),
            "org.basemaps_updated",
            new { Ids = next.Select(e => e.Id).ToArray() }
        );
        return Results.Ok(ToSettings(next));
    }

    private static async Task<IReadOnlyList<Stored>> ReadAsync(
        TenancyDbContext db,
        CancellationToken ct
    )
    {
        var value = await db
            .OrganizationSettings.Where(s => s.Key == SettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<Stored>>(value, Json) ?? [];
        }
        catch (JsonException)
        {
            // a hand-edited setting is not this feature's to guess at: no basemaps, not a 500
            return [];
        }
    }

    private static BasemapSettingsResponse ToSettings(IEnumerable<Stored> entries) =>
        new(
            entries
                .Select(e => new BasemapSetting(
                    e.Id,
                    e.Name,
                    e.UrlTemplate,
                    e.Attribution,
                    e.MaxZoom,
                    e.KeyCipher is not null
                ))
                .ToList()
        );
}
