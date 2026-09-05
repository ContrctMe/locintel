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
    [Transactional(typeof(StorageDbContext))]
    [WolverineGet("/api/files/{id}")]
    [ProducesResponseType(typeof(FileSummary), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid id,
        StorageDbContext db,
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
                f.Id == id && f.Status != FileStatus.Deleted && f.Status != FileStatus.Erased
            )
            .Select(f => new FileSummary(
                f.Id,
                f.Name,
                f.ContentType,
                f.Status.ToString(),
                f.DeletedAt,
                f.LegalHold,
                f.PreviewKey != null,
                f.CreatedAt
            ))
            .SingleOrDefaultAsync(ct);
        return file is null ? Results.NotFound() : Results.Ok(file);
    }
}
