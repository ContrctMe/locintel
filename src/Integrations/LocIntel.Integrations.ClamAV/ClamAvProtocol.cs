using System.Buffers.Binary;
using LocIntel.Platform.Storage;

namespace LocIntel.Integrations.ClamAV;

/// <summary>
/// The clamd INSTREAM wire format as pure functions, so the framing and the
/// verdict parsing are unit-tested without a daemon: each chunk is a 4-byte
/// big-endian length then the bytes; a zero length ends the stream; the
/// reply is one NUL-terminated line. Anything but a clear OK or FOUND is an
/// error - a scanner that cannot answer must never be read as clean.
/// </summary>
public static class ClamAvProtocol
{
    public static ReadOnlyMemory<byte> Command { get; } = "zINSTREAM\0"u8.ToArray();

    public const int MaxChunk = 2048;

    public static byte[] Frame(ReadOnlySpan<byte> chunk)
    {
        var framed = new byte[4 + chunk.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed, (uint)chunk.Length);
        chunk.CopyTo(framed.AsSpan(4));
        return framed;
    }

    public static ReadOnlyMemory<byte> End { get; } = new byte[4];

    public static ScanVerdict ParseVerdict(string reply)
    {
        var line = reply.TrimEnd('\0', '\n', '\r');
        if (line.EndsWith(" OK", StringComparison.Ordinal))
            return ScanVerdict.Clean;
        if (line.EndsWith(" FOUND", StringComparison.Ordinal))
            return ScanVerdict.Infected;
        throw new InvalidOperationException(
            $"clamd did not return a verdict ('{line}'); the file stays quarantined"
        );
    }
}
