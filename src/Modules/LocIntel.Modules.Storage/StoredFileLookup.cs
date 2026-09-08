using LocIntel.Contracts;
using LocIntel.Modules.Storage.Data;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Modules.Storage;

/// <summary>Read contract for modules consuming stored files (ingest).</summary>
public sealed class StoredFileLookup(StorageDbContext db) : IStoredFileLookup
{
    public async Task<StoredFileInfo?> GetAsync(Guid fileId, CancellationToken ct = default)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.Origin == null, ct);
        return file is null
            ? null
            : new StoredFileInfo(
                file.Id,
                file.Key,
                file.Status.ToString(),
                file.ContentType,
                file.Name
            );
    }
}
