using LocIntel.Contracts;
using LocIntel.Modules.Cases.Cases.Api;
using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Notes: anyone working the case adds; the author or a manager removes (tier 2).</summary>
public static class CaseNoteEndpoints
{
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/notes")]
    [ProducesResponseType(typeof(CaseChildAdded), StatusCodes.Status200OK)]
    public static async Task<IResult> Add(
        Guid id,
        AddCaseNoteRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForWork(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Body))
            return ApiErrors.BadRequest("a note needs a body");
        var note = new CaseNote
        {
            Id = Guid.CreateVersion7(),
            OrgId = access!.Case.OrgId,
            CaseId = id,
            AuthorId = access.Actor.Id,
            Body = request.Body.Trim(),
        };
        db.Notes.Add(note);
        access.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CaseChildAdded(note.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverineDelete("/api/cases/{id}/notes/{noteId}")]
    [ProducesResponseType(typeof(CaseChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Remove(
        Guid id,
        Guid noteId,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForWork(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId && n.CaseId == id, ct);
        if (note is null || (note.AuthorId != access!.Actor.Id && !access.Manage))
            return Results.NotFound();
        note.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CaseChildRemoved(note.Id));
    }
}
