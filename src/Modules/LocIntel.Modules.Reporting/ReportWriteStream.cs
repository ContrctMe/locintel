namespace LocIntel.Modules.Reporting;

/// <summary>Seekable file wrapper enforcing a hard output budget, including PDF backpatches.</summary>
internal sealed class ReportWriteStream(Stream inner, long limit) : Stream
{
    private volatile bool _abandoned;

    /// <summary>
    /// Refuse every further byte. A definition renders through synchronous
    /// library calls that cannot be cancelled, so one that outruns its timeout
    /// is abandoned and keeps running on a pool thread. This makes its next
    /// write fail here rather than reach a file the executor already closed.
    /// </summary>
    public void Abandon() => _abandoned = true;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position
    {
        get => inner.Position;
        set
        {
            Check(value);
            inner.Position = value;
        }
    }

    private void Check(long end)
    {
        if (_abandoned)
            throw new IOException("Report output was abandoned after its time budget.");
        if (end < 0 || end > limit)
            throw new IOException("Report output budget exceeded.");
    }

    public override void Flush()
    {
        Check(Position);
        inner.Flush();
    }

    public override Task FlushAsync(CancellationToken ct)
    {
        Check(Position);
        return inner.FlushAsync(ct);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin)
    {
        var end = checked(
            (
                origin == SeekOrigin.Begin ? 0
                : origin == SeekOrigin.Current ? Position
                : Length
            ) + offset
        );
        Check(end);
        return inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        Check(value);
        inner.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Check(checked(Position + count));
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Check(checked(Position + buffer.Length));
        inner.Write(buffer);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken ct = default
    )
    {
        Check(checked(Position + buffer.Length));
        return inner.WriteAsync(buffer, ct);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
}
