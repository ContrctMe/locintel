using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Data;
using LocIntel.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using Wolverine.Attributes;

namespace LocIntel.Modules.Storage;

public static class SaveFilePreviewHandler
{
    [Transactional(typeof(StorageDbContext))]
    public static async Task Handle(
        SaveFilePreview message,
        StorageDbContext db,
        IObjectStore store,
        CancellationToken ct
    )
    {
        if (message.Content.Length > 4096)
            throw new ArgumentOutOfRangeException(nameof(message.Content));
        await db.TakeAsync(message.FileId, ct);
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == message.FileId, ct);
        if (
            file is null
            || file.Status != FileStatus.Clean
            || file.Key != message.Key
            || file.PreviewKey != null
        )
            return;
        var key = FileBytes.PreviewKey(file.Key);
        // ponytail: retain the row lock for this capped write; connection-free writes
        // require a durable write/erase coordination protocol, not best-effort cleanup.
        await store.WriteAsync(
            key,
            new MemoryStream(message.Content, writable: false),
            "text/plain",
            ct
        );
        file.PreviewKey = key;
    }
}
