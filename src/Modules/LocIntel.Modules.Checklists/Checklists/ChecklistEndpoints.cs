using LocIntel.Contracts;
using LocIntel.Modules.Checklists.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Messaging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Checklists.Checklists;

public sealed record CreateTemplateRequest(string Name, string[] Items, string? ScopePath = null);

public sealed record CheckItemRequest(Guid TemplateId, Guid SiteId, int ItemIndex, bool Done);

public sealed record ChecklistItemState(
    int Index,
    string Text,
    bool Done,
    DateTimeOffset? CheckedAt
);

public sealed record ChecklistToday(Guid Id, string Name, IReadOnlyList<ChecklistItemState> Items);

public sealed record ChecklistTodayResponse(
    DateOnly BusinessDate,
    string Site,
    IReadOnlyList<ChecklistToday> Lists
);

public sealed record ChecklistTemplateSummary(
    Guid Id,
    string Name,
    string[] Items,
    string? ScopePath,
    DateTimeOffset CreatedAt
);

/// <summary>
/// The ops archetype's core loop (ADR 45), and the reference vertical slice:
/// this module was scaffolded by tools/new-module.py and consumes Tenancy
/// only through the ISiteDirectory contract. Managers define templates
/// (checklists:manage); site staff tick items (checklists:complete, scope
/// filtered to their subtree); the day is the SITE's business date.
/// </summary>
public static class ChecklistEndpoints
{
    [Transactional(typeof(ChecklistsDbContext))]
    [WolverineGet("/api/checklists/templates")]
    [ProducesResponseType(typeof(List<ChecklistTemplateSummary>), StatusCodes.Status200OK)]
    public static async Task<IResult> ListTemplates(
        ChecklistsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ChecklistsManage, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var templates = await db
            .Templates.OrderBy(t => t.Name)
            .Select(t => new ChecklistTemplateSummary(
                t.Id,
                t.Name,
                t.Items,
                t.ScopePath,
                t.CreatedAt
            ))
            .ToListAsync(ct);
        return Results.Ok(templates);
    }

    [Transactional(typeof(ChecklistsDbContext))]
    [WolverinePost("/api/checklists/templates")]
    public static async Task<IResult> CreateTemplate(
        CreateTemplateRequest request,
        ChecklistsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ChecklistsManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User principal, Org: var org })
            return gate.ToResult();
        var userId = principal.UserId;
        var items = request.Items.Select(i => i.Trim()).Where(i => i.Length > 0).ToArray();
        if (string.IsNullOrWhiteSpace(request.Name) || items.Length == 0)
            return Results.BadRequest(
                new { error = "a checklist needs a name and at least one item" }
            );

        var template = new ChecklistTemplate
        {
            Id = Guid.CreateVersion7(),
            OrgId = org,
            Name = request.Name.Trim(),
            Items = items,
            ScopePath = request.ScopePath,
            CreatedBy = userId,
        };
        db.Templates.Add(template);
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            org,
            AuditActor.User(userId),
            "checklist.template_created",
            new { template.Id, template.Name }
        );
        return Results.Ok(new { template.Id });
    }

    [Transactional(typeof(ChecklistsDbContext))]
    [WolverineDelete("/api/checklists/templates/{id}")]
    public static async Task<IResult> DeleteTemplate(
        Guid id,
        ChecklistsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.ChecklistsManage, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return Results.NotFound();
        // tier 3: configuration goes, the completion trail stays
        await db.Checks.Where(c => c.TemplateId == id).ExecuteDeleteAsync(ct);
        db.Templates.Remove(template);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Today's lists for one site, on that site's clock.</summary>
    [Transactional(typeof(ChecklistsDbContext))]
    [WolverineGet("/api/checklists/today")]
    [ProducesResponseType(typeof(ChecklistTodayResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Today(
        Guid siteId,
        ChecklistsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.ChecklistsComplete, ct);
        if (gate is not GateOutcome.Allowed { Scope: var scope })
            return gate.ToResult();
        var site = await sites.FindAsync(siteId, ct);
        if (site is null || !scope.Covers(site.Path))
            return Results.NotFound();

        var businessDate = BusinessDateFor(site.TimeZone, time);
        var templates = await db.Templates.OrderBy(t => t.Name).ToListAsync(ct);
        var applicable = templates
            .Where(t => t.ScopePath is null || IsUnder(site.Path, t.ScopePath))
            .ToList();
        var templateIds = applicable.Select(t => t.Id).ToList();
        var checks = await db
            .Checks.Where(c =>
                c.SiteId == siteId
                && c.BusinessDate == businessDate
                && templateIds.Contains(c.TemplateId)
            )
            .ToListAsync(ct);

        return Results.Ok(
            new ChecklistTodayResponse(
                businessDate,
                site.Name,
                applicable
                    .Select(t => new ChecklistToday(
                        t.Id,
                        t.Name,
                        t.Items.Select(
                                (text, index) =>
                                {
                                    var check = checks.FirstOrDefault(c =>
                                        c.TemplateId == t.Id && c.ItemIndex == index
                                    );
                                    return new ChecklistItemState(
                                        index,
                                        text,
                                        check is not null,
                                        check?.CheckedAt
                                    );
                                }
                            )
                            .ToList()
                    ))
                    .ToList()
            )
        );
    }

    [Transactional(typeof(ChecklistsDbContext))]
    [WolverinePost("/api/checklists/check")]
    public static async Task<IResult> Check(
        CheckItemRequest request,
        ChecklistsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISiteDirectory sites,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireUserAsync(
            accessor,
            scopes,
            Capabilities.ChecklistsComplete,
            ct
        );
        if (
            gate
            is not GateOutcome.Allowed
            {
                Principal: Principal.User { UserId: var userId },
                Org: var org,
                Scope: var scope,
            }
        )
            return gate.ToResult();
        var site = await sites.FindAsync(request.SiteId, ct);
        if (site is null || !scope.Covers(site.Path))
            return Results.NotFound();
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct);
        if (template is null || request.ItemIndex < 0 || request.ItemIndex >= template.Items.Length)
            return Results.NotFound();

        var businessDate = BusinessDateFor(site.TimeZone, time);
        var existing = await db.Checks.FirstOrDefaultAsync(
            c =>
                c.TemplateId == request.TemplateId
                && c.SiteId == request.SiteId
                && c.BusinessDate == businessDate
                && c.ItemIndex == request.ItemIndex,
            ct
        );
        if (request.Done && existing is null)
            db.Checks.Add(
                new ChecklistItemCheck
                {
                    Id = Guid.CreateVersion7(),
                    OrgId = org,
                    TemplateId = request.TemplateId,
                    SiteId = request.SiteId,
                    BusinessDate = businessDate,
                    ItemIndex = request.ItemIndex,
                    CheckedBy = userId,
                }
            );
        else if (!request.Done && existing is not null)
            db.Checks.Remove(existing);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>"Today" on the SITE's clock (ADR 26 kind 3: stamped site-local business date).</summary>
    private static DateOnly BusinessDateFor(string timeZone, TimeProvider time) =>
        DateOnly.FromDateTime(
            TimeZoneInfo
                .ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(timeZone))
                .DateTime
        );

    /// <summary>ltree containment without the extension: label-prefix match.</summary>
    private static bool IsUnder(string path, string prefix) =>
        path == prefix || path.StartsWith(prefix + ".");
}
