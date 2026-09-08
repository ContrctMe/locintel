using LocIntel.Modules.Storage.Data;
using LocIntel.Platform.Storage;

namespace LocIntel.Modules.Storage;

public static class FileBytes
{
    public static string PreviewKey(string key) => key + ".preview.txt";

    public static async Task EraseAsync(FileObject file, IObjectStore store, CancellationToken ct)
    {
        await store.DeleteAsync(file.Key, ct);
        // Also erase bytes left by a preview write whose metadata commit failed.
        var deterministicPreview = PreviewKey(file.Key);
        await store.DeleteAsync(deterministicPreview, ct);
        if (file.PreviewKey is { } legacy && legacy != deterministicPreview)
            await store.DeleteAsync(legacy, ct);
    }
}
