using LocIntel.Contracts.Storage;
using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Storage;

/// <summary>Signs for other modules exactly as the download endpoint does: Clean files only, five minutes.</summary>
public sealed class SignedFileAccess(StorageDbContext db, IObjectStore store) : ISignedFileAccess
{
    public async Task<SignedFileUrl?> GetDownloadUrlAsync(
        Guid fileId,
        CancellationToken ct = default
    )
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null || file.Status != FileStatus.Clean)
            return null;
        var url = await store.GetDownloadUrlAsync(file.Key, TimeSpan.FromMinutes(5), ct);
        return new SignedFileUrl(url.ToString(), 300, file.Name, file.ContentType);
    }
}
