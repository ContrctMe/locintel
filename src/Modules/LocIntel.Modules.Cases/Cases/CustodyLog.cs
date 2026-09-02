using LocIntel.Modules.Cases.Data;
using LocIntel.Platform.Kernel;

namespace LocIntel.Modules.Cases.Cases;

/// <summary>Appends to the chain of custody; the caller saves.</summary>
public static class CustodyLog
{
    public static void Record(
        CasesDbContext db,
        Case @case,
        Guid fileId,
        CustodyAction action,
        ActorRef actor,
        string? detail = null
    ) =>
        db.Custody.Add(
            new CustodyEvent
            {
                Id = Guid.CreateVersion7(),
                OrgId = @case.OrgId,
                CaseId = @case.Id,
                FileId = fileId,
                Action = action,
                ActorId = actor.Id,
                ActorTier = actor.Tier,
                Detail = detail,
            }
        );
}
