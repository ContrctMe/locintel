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

    public async Task<IReadOnlyDictionary<Guid, StoredFileInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> fileIds,
        CancellationToken ct = default
    )
    {
        var result = new Dictionary<Guid, StoredFileInfo>();
        foreach (var ids in fileIds.Distinct().Chunk(1000))
        {
            var files = await db
                .Files.AsNoTracking()
                .Where(f => ids.Contains(f.Id) && f.Origin == null)
                .Select(f => new StoredFileInfo(
                    f.Id,
                    f.Key,
                    f.Status.ToString(),
                    f.ContentType,
                    f.Name
                ))
                .ToListAsync(ct);
            foreach (var file in files)
                result.Add(file.Id, file);
        }
        return result;
    }
}
