using LocIntel.Modules.Tenancy.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Tenancy.Organizations;

public sealed record PublicUrlResponse(string Url, string EmbedSnippet);

/// <summary>
/// Where does this org live on the public internet? (ADR 43). The API is the
/// one place that knows Public:HostTemplate, so it also renders the embed
/// snippet the console shows - members paste it into their own website.
/// </summary>
public static class PublicUrlEndpoint
{
    [Transactional(typeof(TenancyDbContext))]
    [WolverineGet("/api/org/public-url")]
    [ProducesResponseType(typeof(PublicUrlResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        TenancyDbContext db,
        IPrincipalAccessor accessor,
        IConfiguration configuration,
        CancellationToken ct
    )
    {
        if (accessor.Current is not Principal.User { ActiveOrg: { } org })
            return Results.Unauthorized();
        var slug = await db
            .Organizations.Where(o => o.Id == org)
            .Select(o => o.Slug)
            .FirstAsync(ct);
        var template = configuration["Public:HostTemplate"] ?? "http://{slug}.localhost:5174";
        var url = template.Replace("{slug}", slug);
        var snippet =
            $"""<iframe src="{url}/embed" style="width:100%;height:480px;border:0;border-radius:8px" title="Our locations"></iframe>""";
        return Results.Ok(new PublicUrlResponse(url, snippet));
    }
}
