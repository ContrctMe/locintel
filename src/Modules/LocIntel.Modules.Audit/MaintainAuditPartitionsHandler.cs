using LocIntel.Modules.Audit.Data;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Audit;

public static class MaintainAuditPartitionsHandler
{
    [Transactional(typeof(AuditDbContext))]
    public static async Task Handle(
        MaintainAuditPartitions _,
        AuditDbContext db,
        CancellationToken ct
    )
    {
        if (db.Tenant.OrgId is not null)
            throw new InvalidOperationException("Partition maintenance must not carry a tenant");
        // SECURITY DEFINER functions own DDL and serialize against partition
        // writers. The transaction also makes ensure + prune atomic on retry.
        await db.Database.ExecuteSqlRawAsync(
            "SELECT audit.ensure_access_log_partitions(); SELECT audit.prune_access_log_partitions(400);",
            ct
        );
    }
}
