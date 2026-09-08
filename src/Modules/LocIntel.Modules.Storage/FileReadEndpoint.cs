using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Kernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Storage;

public static class FileReadEndpoint
{
    [Transactional(
        typeof(StorageDbContext),
        Mode = Wolverine.Persistence.TransactionMiddlewareMode.Lightweight
    )]
    [WolverineGet("/api/files/{id}")]
    [ProducesResponseType(typeof(FileSummary), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        var gate = await Gate.RequireAsync(accessor, scopes, Capabilities.FilesRead, ct);
        if (gate is not GateOutcome.Allowed)
            return gate.ToResult();
        // Tenant filters/RLS still apply. Poll a stable identity, not the newest
        // list page, and expose neither bytes nor storage keys before scanning.
        var file = await db
            .Files.Where(f =>
                f.Id == id
                && f.Status != FileStatus.Deleted
                && f.Status != FileStatus.Erased
                && f.DeletedAt == null
            )
            .SingleOrDefaultAsync(ct);
        return
            file is null
            || !await access.AllowsAsync(file, accessor.Current, Capabilities.FilesRead, ct)
            ? Results.NotFound()
            : Results.Ok(FileEndpoints.View(file));
    }
}
