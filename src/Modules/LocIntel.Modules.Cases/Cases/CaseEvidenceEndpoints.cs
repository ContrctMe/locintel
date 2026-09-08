using LocIntel.Contracts;
using LocIntel.Contracts.Storage;
using LocIntel.Modules.Cases.Cases.Api;
using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>
/// Evidence: files already in Storage, held by id. Every touch lands in the
/// custody chain - adding, downloading (through the case, so it is
/// attributable), removing. Under legal hold nothing leaves the case, and a
/// file added to a held case is held on arrival.
/// </summary>
public static class CaseEvidenceEndpoints
{
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/evidence")]
    [ProducesResponseType(typeof(CaseChildAdded), StatusCodes.Status200OK)]
    public static async Task<IResult> Add(
        Guid id,
        AddCaseEvidenceRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IStoredFileLookup files,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForWork(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var file = await files.GetAsync(request.FileId, ct);
        if (file is null || file.Status is "Deleted" or "Erased")
            return Results.NotFound();
        if (await db.Evidence.AnyAsync(e => e.CaseId == id && e.FileId == file.Id, ct))
            return Results.Conflict(new { error = "file is already evidence on this case" });
        var @case = access!.Case;
        var item = new CaseEvidence
        {
            Id = Guid.CreateVersion7(),
            OrgId = @case.OrgId,
            CaseId = id,
            FileId = file.Id,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            AddedBy = access.Actor.Id,
        };
        db.Evidence.Add(item);
        CustodyLog.Record(db, @case, file.Id, CustodyAction.Added, access.Actor, file.Name);
        if (@case.LegalHold)
            CustodyLog.Record(
                db,
                @case,
                file.Id,
                CustodyAction.HoldPlaced,
                access.Actor,
                "case under hold"
            );
        @case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        if (@case.LegalHold)
            await bus.PublishAsync(
                new FileHoldRequested(file.Id, true, $"case {@case.Id}"),
                new DeliveryOptions { TenantId = @case.OrgId.Value.ToString() }
            );
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.evidence_added",
            new
            {
                CaseId = id,
                FileId = file.Id,
                file.Name,
            }
        );
        return Results.Ok(new CaseChildAdded(item.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverineDelete("/api/cases/{id}/evidence/{evidenceId}")]
    [ProducesResponseType(typeof(CaseChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Remove(
        Guid id,
        Guid evidenceId,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var @case = access!.Case;
        if (@case.LegalHold)
            return Results.Conflict(new { error = "case is under legal hold" });
        var item = await db.Evidence.FirstOrDefaultAsync(
            e => e.Id == evidenceId && e.CaseId == id,
            ct
        );
        if (item is null)
            return Results.NotFound();
        db.Evidence.Remove(item); // the link goes; the custody chain keeps the removal
        CustodyLog.Record(db, @case, item.FileId, CustodyAction.Removed, access.Actor);
        @case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.evidence_removed",
            new { CaseId = id, item.FileId }
        );
        return Results.Ok(new CaseChildRemoved(item.Id));
    }

    /// <summary>Authorize and record access issuance before returning the signed URL; issuance is not delivery.</summary>
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/evidence/{evidenceId}/download")]
    [ProducesResponseType(typeof(CaseEvidenceDownload), StatusCodes.Status200OK)]
    public static async Task<IResult> Download(
        Guid id,
        Guid evidenceId,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        ISignedFileAccess signer,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseAccess.LoadAsync(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        if (!access!.CanWork)
            return new GateOutcome.Forbidden(Capabilities.CasesManage).ToResult();
        var item = await db.Evidence.FirstOrDefaultAsync(
            e => e.Id == evidenceId && e.CaseId == id,
            ct
        );
        if (item is null)
            return Results.NotFound();
        var signed = await signer.GetDownloadUrlAsync(item.FileId, ct);
        if (signed is null)
            return Results.NotFound();
        CustodyLog.Record(
            db,
            access.Case,
            item.FileId,
            CustodyAction.DownloadAccessIssued,
            access.Actor
        );
        await db.SaveChangesAsync(ct);
        await bus.AuditAsync(
            access.Actor.Org,
            access.Actor.Audit,
            "case.evidence_download_access_issued",
            new { CaseId = id, item.FileId }
        );
        return Results.Ok(
            new CaseEvidenceDownload(
                signed.Url,
                signed.ExpiresInSeconds,
                signed.FileName,
                signed.ContentType
            )
        );
    }
}
