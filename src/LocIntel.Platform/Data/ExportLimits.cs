using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace LocIntel.Platform.Data;

/// <summary>Bound materialization at the database reader, before constructing an export document.</summary>
public static class ExportLimits
{
    public const int MaxRows = 100_000;
    public const int MaxSectionBytes = 4 * 1024 * 1024;
    public const int MaxArchiveBytes = 32 * 1024 * 1024;

    public static async Task<List<T>> ToBoundedExportListAsync<T>(
        this IQueryable<T> query,
        CancellationToken ct
    )
    {
        var rows = new List<T>();
        long bytes = 0;
        await foreach (var row in query.Take(MaxRows + 1).AsAsyncEnumerable().WithCancellation(ct))
        {
            bytes += JsonSerializer.SerializeToUtf8Bytes(row, JsonSerializerOptions.Web).Length;
            if (rows.Count == MaxRows || bytes > MaxSectionBytes)
                throw new InvalidDataException("Export section exceeds its row or byte budget.");
            rows.Add(row);
        }
        return rows;
    }

    public static long AddSection(long bytes, string json)
    {
        bytes += Encoding.UTF8.GetByteCount(json);
        return bytes <= MaxArchiveBytes
            ? bytes
            : throw new InvalidDataException("Export exceeds its archive byte budget.");
    }
}
