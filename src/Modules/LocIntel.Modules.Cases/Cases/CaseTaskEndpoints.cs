using LocIntel.Modules.Cases.Cases.Api;
using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Work items: anyone working the case (member or manager) adds, ticks, and removes them.</summary>
public static class CaseTaskEndpoints
{
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/tasks")]
    [ProducesResponseType(typeof(CaseChildAdded), StatusCodes.Status200OK)]
    public static async Task<IResult> Add(
        Guid id,
        AddCaseTaskRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForWork(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
            return Results.BadRequest(
                new { error = "a task needs a title of up to 200 characters" }
            );
        var task = new CaseTask
        {
            Id = Guid.CreateVersion7(),
            OrgId = access!.Case.OrgId,
            CaseId = id,
            Title = request.Title.Trim(),
            AssigneeId = request.AssigneeId,
            DueAt = request.DueAt?.ToUniversalTime(),
            CreatedBy = access.Actor.Id,
        };
        db.Tasks.Add(task);
        access.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CaseChildAdded(task.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/tasks/{taskId}/done")]
    [ProducesResponseType(typeof(CaseTaskMutated), StatusCodes.Status200OK)]
    public static async Task<IResult> SetDone(
        Guid id,
        Guid taskId,
        SetTaskDoneRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForWork(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.CaseId == id, ct);
        if (task is null)
            return Results.NotFound();
        task.DoneAt = request.Done ? DateTimeOffset.UtcNow : null;
        task.DoneBy = request.Done ? access!.Actor.Id : null;
        access!.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CaseTaskMutated(task.Id, task.DoneAt));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverineDelete("/api/cases/{id}/tasks/{taskId}")]
    [ProducesResponseType(typeof(CaseChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Remove(
        Guid id,
        Guid taskId,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForWork(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.CaseId == id, ct);
        if (task is null)
            return Results.NotFound();
        task.DeletedAt = DateTimeOffset.UtcNow; // tier 2
        access!.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new CaseChildRemoved(task.Id));
    }
}
