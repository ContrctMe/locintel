using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents.Api;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>Notes ride the incident's scope: reporters add, managers (or the author) remove.</summary>
public static class IncidentNoteEndpoints
{
    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/{id}/notes")]
    [ProducesResponseType(typeof(IncidentNoteCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Add(
        Guid id,
        AddIncidentNoteRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (incident, actor, error) = await IncidentEndpoints.LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsReport,
            ct
        );
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest(new { error = "a note needs a body" });
        var note = new IncidentNote
        {
            Id = Guid.CreateVersion7(),
            OrgId = incident!.OrgId,
            IncidentId = incident.Id,
            AuthorId = actor!.Value.Id,
            Body = request.Body.Trim(),
        };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new IncidentNoteCreated(note.Id));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverineDelete("/api/incidents/{id}/notes/{noteId}")]
    [ProducesResponseType(typeof(IncidentChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Remove(
        Guid id,
        Guid noteId,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (_, actor, error) = await IncidentEndpoints.LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsReport,
            ct
        );
        if (error is not null)
            return error;
        var note = await db.Notes.FirstOrDefaultAsync(
            n => n.Id == noteId && n.IncidentId == id,
            ct
        );
        if (note is null)
            return Results.NotFound();
        if (
            note.AuthorId != actor!.Value.Id
            && !await scopes.CanAsync(accessor.Current, Capabilities.IncidentsManage, ct)
        )
            return Results.NotFound();
        note.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new IncidentChildRemoved(noteId));
    }
}
