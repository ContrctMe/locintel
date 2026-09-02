using LocIntel.Modules.Identity.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Identity.Access;

public sealed class OperatorContext(IdentityDbContext db, IScopeResolver scopes) : IOperatorContext
{
    public async ValueTask<bool> IsOperatorAsync(
        Principal principal,
        CancellationToken ct = default
    )
    {
        if (principal is not Principal.User { ActiveOrg: { } org })
            return false;
        var entry = await db.OrgDirectory.FirstOrDefaultAsync(d => d.OrgId == org, ct);
        return entry is { IsPlatform: true }
            && await scopes.CanAsync(principal, Capabilities.PlatformOperate, ct);
    }
}
