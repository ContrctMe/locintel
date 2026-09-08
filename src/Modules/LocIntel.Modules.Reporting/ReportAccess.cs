using System.Text.Json;
using LocIntel.Contracts;
using LocIntel.Modules.Reporting.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Reporting;

public sealed class ReportAccess(
    IReportRequester requesters,
    IScopeResolver scopes,
    ISiteSource sites
)
{
    /// <summary>
    /// The reader's identity and read scope for this request. Null when the
    /// principal is no longer an active member: a removed requester reads nothing.
    /// </summary>
    public async Task<ReportReader?> ReaderAsync(OrgId org, Guid userId, CancellationToken ct)
    {
        var user = await requesters.GetActiveAsync(org, userId, ct);
        return user is null
            ? null
            : new ReportReader(
                user,
                Intersect(
                    await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
                    await scopes.ScopeForAsync(user, Capabilities.FilesRead, ct)
                )
            );
    }

    /// <summary>
    /// Names of the sites in <paramref name="ids"/> this reader may see. An absent
    /// id is one the reader cannot see; callers requiring the whole set compare counts.
    /// One query for a whole page of runs, not one per run.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, string>> VisibleSitesAsync(
        OrgId org,
        ReportReader reader,
        Guid[] ids,
        CancellationToken ct
    ) =>
        ids.Length == 0
            ? new Dictionary<Guid, string>()
            : (await sites.SelectAsync(org, reader.Scope, ids, ids.Length, ct)).ToDictionary(
                x => x.Id,
                x => x.Name
            );

    public async Task<bool> CanReadSitesAsync(
        OrgId org,
        Guid userId,
        Guid[] ids,
        CancellationToken ct
    )
    {
        var reader = await ReaderAsync(org, userId, ct);
        return reader is not null && await CanReadSitesAsync(org, reader, ids, ct);
    }

    public async Task<bool> CanReadSitesAsync(
        OrgId org,
        ReportReader reader,
        Guid[] ids,
        CancellationToken ct
    ) => ids.Length > 0 && (await VisibleSitesAsync(org, reader, ids, ct)).Count == ids.Length;

    /// <summary>
    /// Whether the reader may see this run. <paramref name="visible"/> is the
    /// already-resolved visible set for the whole page; every one of the run's
    /// sites must be in it. The definition still authorizes each item itself -
    /// that is the fork's extension point and cannot be batched away.
    /// </summary>
    public async Task<bool> CanReadJobAsync(
        ReportJob job,
        ReportReader reader,
        IReadOnlySet<Guid> visible,
        ReportRegistry registry,
        CancellationToken ct
    )
    {
        if (
            job.State == ReportJobState.Purging
            || job.SiteIds.Length == 0
            || !job.SiteIds.All(visible.Contains)
        )
            return false;
        var definition = registry.Find(job.ReportType, job.DefinitionVersion);
        if (definition is null)
            return false;
        foreach (var item in job.Items)
            if (
                !await definition.AuthorizeAsync(
                    Request(job, item.SiteIds) with
                    {
                        UserId = reader.User.UserId,
                    },
                    item.State == ReportItemState.Succeeded
                        ? JsonSerializer.Deserialize<JsonElement>(item.DependenciesJson)
                        : null,
                    ct
                )
            )
                return false;
        return true;
    }

    public async Task<bool> CanGenerateAsync(OrgId org, Guid userId, CancellationToken ct)
    {
        var user = await requesters.GetActiveAsync(org, userId, ct);
        return user is not null && await scopes.CanAsync(user, Capabilities.ReportsGenerate, ct);
    }

    /// <summary>
    /// Generation scope: site-read intersected with reports-generate. Distinct from
    /// the read scope, which pairs site-read with file-read.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, string>> GeneratableSitesAsync(
        OrgId org,
        Guid userId,
        Guid[] ids,
        CancellationToken ct
    )
    {
        var user = await requesters.GetActiveAsync(org, userId, ct);
        if (user is null || ids.Length == 0)
            return new Dictionary<Guid, string>();
        var scope = Intersect(
            await scopes.ScopeForAsync(user, Capabilities.SitesRead, ct),
            await scopes.ScopeForAsync(user, Capabilities.ReportsGenerate, ct)
        );
        return (await sites.SelectAsync(org, scope, ids, ids.Length, ct)).ToDictionary(
            x => x.Id,
            x => x.Name
        );
    }

    public async Task<bool> CanAccessSitesAsync(
        OrgId org,
        Guid userId,
        Guid[] ids,
        CancellationToken ct
    ) => ids.Length > 0 && (await GeneratableSitesAsync(org, userId, ids, ct)).Count == ids.Length;

    public static NodeScope Intersect(NodeScope left, NodeScope right)
    {
        if (left is NodeScope.None || right is NodeScope.None)
            return NodeScope.Nothing;
        if (left is NodeScope.EntireOrg)
            return right;
        if (right is NodeScope.EntireOrg)
            return left;
        var a = (NodeScope.Subtrees)left;
        var b = (NodeScope.Subtrees)right;
        if (a.Org != b.Org)
            return NodeScope.Nothing;
        return new NodeScope.Subtrees(
            a.Org,
            a.Paths.Concat(b.Paths).Distinct().Where(p => a.Covers(p) && b.Covers(p)).ToArray()
        );
    }

    public static IReportDefinition.Request Request(ReportJob job, Guid[] sites) =>
        new(
            job.OrgId,
            job.RequestedBy,
            sites,
            job.Selection,
            JsonSerializer.Deserialize<JsonElement>(job.OptionsJson)
        );
}
