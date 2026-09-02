using LocIntel.Contracts;
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

/// <summary>Who works the case. Membership is need-to-know for the case, so it is a managed, audited change.</summary>
public static class CaseMemberEndpoints
{
    [Transactional(typeof(CasesDbContext))]
    [WolverinePost("/api/cases/{id}/members")]
    [ProducesResponseType(typeof(CaseChildAdded), StatusCodes.Status200OK)]
    public static async Task<IResult> Add(
        Guid id,
        AddCaseMemberRequest request,
        CasesDbContext db,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        IActorDirectory actors,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var (access, error) = await CaseEndpoints.LoadForManage(id, db, accessor, scopes, ct);
        if (error is not null)
            return error;
        var known = await actors.LabelsAsync([request.UserId], ct);
        if (!known.ContainsKey(request.UserId))
            return Results.NotFound();
        if (await db.Members.AnyAsync(m => m.CaseId == id && m.UserId == request.UserId, ct))
            return Results.Conflict(new { error = "already a member" });
        var member = new CaseMember
        {
            Id = Guid.CreateVersion7(),
            OrgId = access!.Case.OrgId,
            CaseId = id,
            UserId = request.UserId,
            Role = request.Role,
            AddedBy = access.Actor.Id,
        };
        db.Members.Add(member);
        if (request.Role == CaseMemberRole.Lead)
            access.Case.LeadId = request.UserId;
        access.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await CaseAudit.PublishAsync(
            bus,
            access.Actor,
            "case.member_added",
            new
            {
                CaseId = id,
                request.UserId,
                Role = request.Role.ToString(),
            }
        );
        return Results.Ok(new CaseChildAdded(member.Id));
    }

    [Transactional(typeof(CasesDbContext))]
    [WolverineDelete("/api/cases/{id}/members/{memberId}")]
    [ProducesResponseType(typeof(CaseChildRemoved), StatusCodes.Status200OK)]
    public static async Task<IResult> Remove(
        Guid id,
        Guid memberId,
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
        var member = await db.Members.FirstOrDefaultAsync(
            m => m.Id == memberId && m.CaseId == id,
            ct
        );
        if (member is null)
            return Results.NotFound();
        db.Members.Remove(member);
        if (access!.Case.LeadId == member.UserId)
            access.Case.LeadId = null;
        access.Case.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await CaseAudit.PublishAsync(
            bus,
            access.Actor,
            "case.member_removed",
            new { CaseId = id, member.UserId }
        );
        return Results.Ok(new CaseChildRemoved(member.Id));
    }
}
