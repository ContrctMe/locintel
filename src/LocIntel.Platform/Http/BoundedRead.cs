namespace LocIntel.Platform.Http;

public static class BoundedRead
{
    public static async Task<byte[]> ReadAsync(Stream source, int maxBytes, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + read > maxBytes)
                throw new InvalidDataException($"Content exceeds {maxBytes} bytes.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
