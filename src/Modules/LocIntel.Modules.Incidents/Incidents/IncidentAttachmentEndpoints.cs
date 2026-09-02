using LocIntel.Contracts;
using LocIntel.Modules.Incidents.Data;
using LocIntel.Modules.Incidents.Incidents.Api;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Incidents.Incidents;

/// <summary>
/// Attach an already-stored file by id. Storage's lookup is tenant-filtered,
/// so another org's file id is simply absent (404); a trashed or erased file
/// cannot be attached.
/// </summary>
public static class IncidentAttachmentEndpoints
{
    [Transactional(typeof(IncidentsDbContext))]
    [WolverinePost("/api/incidents/{id}/attachments")]
    [ProducesResponseType(typeof(IncidentAttachmentCreated), StatusCodes.Status200OK)]
    public static async Task<IResult> Attach(
        Guid id,
        AttachFileRequest request,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IStoredFileLookup files,
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
        var file = await files.GetAsync(request.FileId, ct);
        if (file is null || file.Status is "Deleted" or "Erased")
            return Results.NotFound();
        if (await db.Attachments.AnyAsync(a => a.IncidentId == id && a.FileId == file.Id, ct))
            return Results.Conflict(new { error = "file is already attached" });
        var attachment = new IncidentAttachment
        {
            Id = Guid.CreateVersion7(),
            OrgId = incident!.OrgId,
            IncidentId = incident.Id,
            FileId = file.Id,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            AddedBy = actor!.Value.Id,
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new IncidentAttachmentCreated(attachment.Id));
    }

    [Transactional(typeof(IncidentsDbContext))]
    [WolverineDelete("/api/incidents/{id}/attachments/{attachmentId}")]
    [ProducesResponseType(typeof(IncidentChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Detach(
        Guid id,
        Guid attachmentId,
        IncidentsDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var (incident, _, error) = await IncidentEndpoints.LoadForWrite(
            id,
            db,
            accessor,
            scopes,
            Capabilities.IncidentsManage,
            ct
        );
        if (error is not null)
            return error;
        if (incident!.LegalHold)
            return Results.Conflict(new { error = "incident is under legal hold" });
        var attachment = await db.Attachments.FirstOrDefaultAsync(
            a => a.Id == attachmentId && a.IncidentId == id,
            ct
        );
        if (attachment is null)
            return Results.NotFound();
        db.Attachments.Remove(attachment); // tier 3: the link goes, the file stays in Storage
        await db.SaveChangesAsync(ct);
        return Results.Ok(new IncidentChildRemoved(attachmentId));
    }
}
