using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting;

public sealed class ReferenceReportAccess(
    IReportRequester requesters,
    IScopeResolver scopes,
    IReportFileSource files,
    ISiteSource sites,
    IReportOverlaySource overlays
)
{
    public async Task<bool> AuthorizeAsync(
        IReportDefinition.Request request,
        JsonElement? dependencies,
        CancellationToken ct
    )
    {
        var user = await requesters.GetActiveAsync(request.Org, request.UserId, ct);
        if (user is null)
            return false;
        var options = ReferenceReportOptions.Parse(request.Options);
        var used = dependencies is { } captured
            ? captured.Deserialize<ReportVisualDependencies>()
            : new ReportVisualDependencies(options.PhotosFor(request.SiteIds), options.OverlayIds);
        if (used?.PhotoIds is null || used.OverlayIds is null)
            return false;
        if (used.PhotoIds.Length > 0)
        {
            if (!await scopes.CanAsync(user, Capabilities.FilesRead, ct))
                return false;
            var found = await files.ReadAsync(request.Org, used.PhotoIds, ct);
            if (found.Count != used.PhotoIds.Length)
                return false;
            var siteIds = found.SelectMany(x => x.SiteIds).Distinct().ToArray();
            if (siteIds.Length > 1000)
                return false;
            if (siteIds.Length > 0)
            {
                var scope = ReportAccess.Intersect(
                    await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
                    await scopes.ScopeForAsync(user, Capabilities.FilesRead, ct)
                );
                if (
                    (await sites.SelectAsync(request.Org, scope, siteIds, siteIds.Length, ct)).Count
                    != siteIds.Length
                )
                    return false;
            }
        }
        if (used.OverlayIds.Length > 0)
        {
            var scope = await scopes.ScopeForAsync(user, Capabilities.OverlaysRead, ct);
            var found = await overlays.ReadAsync(request.Org, scope, used.OverlayIds, ct);
            if (found.Count != used.OverlayIds.Length)
                return false;
        }
        return true;
    }
}
