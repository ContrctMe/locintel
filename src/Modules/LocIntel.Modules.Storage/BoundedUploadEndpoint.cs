using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using LocIntel.Platform.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;
using Wolverine.Http;

namespace LocIntel.Modules.Storage;

/// <summary>For providers whose signed tickets cannot enforce bytes (Azure SAS).</summary>
public static class BoundedUploadEndpoint
{
    // ponytail: one relay per process fits the 256 MiB /tmp; raise concurrency only with a larger scratch budget.
    private static readonly SemaphoreSlim UploadSlot = new(1, 1);

    [Transactional(typeof(StorageDbContext))]
    [WolverinePut("/api/files/{id}/content")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<IResult> Put(
        Guid id,
        HttpContext http,
        StorageDbContext db,
        [FromServices] FileAccess access,
        IObjectStore store,
        IPrincipalAccessor accessor,
        IScopeResolver scopes,
        CancellationToken ct
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        ct = deadline.Token;
        var gate = await Gate.RequireUserAsync(accessor, scopes, Capabilities.FilesManage, ct);
        if (gate is not GateOutcome.Allowed { Principal: Principal.User user })
            return gate.ToResult();
        if (!await UploadSlot.WaitAsync(0, ct))
        {
            http.Response.Headers.RetryAfter = "2";
            return ApiErrors.Status(
                "upload relay is busy; retry",
                StatusCodes.Status503ServiceUnavailable
            );
        }
        try
        {
            await db.TakeAsync(id, ct);
            var file = await db.Files.FirstOrDefaultAsync(
                f => f.Id == id && f.CreatedBy == user.UserId,
                ct
            );
            if (
                store.SupportsBoundedUpload
                || file is null
                || file.Status != FileStatus.PendingUpload
                || file.CreatedAt.AddMinutes(15) <= DateTimeOffset.UtcNow
            )
                return Results.NotFound();
            if (!await access.AllowsAsync(file, user, Capabilities.FilesManage, ct))
                return Results.NotFound();
            if (await store.GetLengthAsync(file.Key, ct) is not null)
                return ApiErrors.Conflict("this upload ticket was already used");
            if (http.Request.ContentLength > file.MaxBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            // Stage before storage: no partial or oversized remote object on a failed body.
            var path = Path.Combine(Path.GetTempPath(), $"locintel-upload-{Guid.NewGuid():N}");
            await using var staged = new FileStream(
                path,
                FileMode.CreateNew,
                System.IO.FileAccess.ReadWrite,
                FileShare.None,
                65536,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose
            );
            var buffer = new byte[65536];
            int count;
            while ((count = await http.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (staged.Length + count > file.MaxBytes)
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                await staged.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (staged.Length == 0)
                return ApiErrors.BadRequest("empty upload");
            staged.Position = 0;
            await store.WriteAsync(file.Key, staged, file.ContentType, ct);
            return Results.NoContent();
        }
        finally
        {
            UploadSlot.Release();
        }
    }
}
