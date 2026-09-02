using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents.Api;
using LocIntel.Platform.Data;
using LocIntel.Platform.Entitlements;
using LocIntel.Platform.Intelligence;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// AI assistance on an incident: suggest category, severity, tags, and a
/// one-line summary from the narrative. Gate 1 is ai.assist; the reader
/// must already be able to see the incident. Nothing is written - the
/// suggestion returns to the person, who applies it (or not).
/// </summary>
public static class IncidentAssistEndpoint
{
    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/{id}/assist")]
    [ProducesResponseType(typeof(IncidentAssistResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Suggest(
        Guid id,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IEntitlements entitlements,
        ITextIntelligence intelligence,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (ActorRef.From(accessor.Current) is not { } actor)
            return Results.Unauthorized();
        var scope = await scopes.ScopeForAsync(accessor.Current, Capabilities.IncidentsReport, ct);
        if (scope is NodeScope.None)
            return Results.Unauthorized();
        if (!await entitlements.HasAsync(actor.Org, EntitlementCatalog.AiAssist, ct))
            return Results.Json(
                new
                {
                    error = "AI assistance is not part of this plan",
                    code = EntitlementCatalog.AiAssist,
                },
                statusCode: StatusCodes.Status402PaymentRequired
            );
        var incident = await db.Incidents.InScope(scope).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (incident is null)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(incident.Narrative) && incident.Title.Length < 10)
            return Results.BadRequest(
                new { error = "add a narrative first; there is nothing to classify" }
            );

        var suggestion = await intelligence.SuggestIncidentAsync(
            new IncidentSuggestionInput(
                incident.Title,
                incident.Narrative,
                Enum.GetNames<IncidentCategory>(),
                Enum.GetNames<IncidentSeverity>()
            ),
            ct
        );
        var category = Enum.TryParse<IncidentCategory>(suggestion.Category, true, out var c)
            ? c
            : incident.Category;
        var severity = Enum.TryParse<IncidentSeverity>(suggestion.Severity, true, out var s)
            ? s
            : incident.Severity;
        await IncidentAudit.PublishAsync(
            bus,
            actor,
            "incident.assist_requested",
            new
            {
                incident.Id,
                intelligence.Provider,
                Category = category.ToString(),
                Severity = severity.ToString(),
            }
        );
        return Results.Ok(
            new IncidentAssistResponse(
                intelligence.Provider,
                category,
                severity,
                suggestion
                    .Tags.Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => t.Length > 0)
                    .Distinct()
                    .Take(10)
                    .ToArray(),
                suggestion.Summary,
                suggestion.Confidence
            )
        );
    }
}
