using SkiaSharp;

namespace LocIntel.Modules.Reporting.Rendering;

/// <summary>Only JPEG/PNG, bounded before pixel allocation; normalize orientation and discard metadata.</summary>
public static class ReportImages
{
    public const int MaxEncodedBytes = 8 * 1024 * 1024;
    public const int MaxPixels = 20_000_000;
    public const int MaxDimension = 10_000;
    public const int OutputDimension = 1600;

    public static async Task<byte[]> ReadAsync(
        Stream source,
        CancellationToken ct,
        bool preserveLossless = false
    )
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > MaxEncodedBytes)
                throw new InvalidDataException("Image exceeds encoded byte limit.");
            buffer.Write(chunk, 0, read);
        }
        ct.ThrowIfCancellationRequested();
        return Normalize(buffer.ToArray(), preserveLossless);
    }

    /// <param name="preserveLossless">
    /// Re-encode a PNG source as PNG. Map tiles are flat colour, hard edges and
    /// small labels - exactly what JPEG blurs - so they keep their format.
    /// Photographs stay JPEG, where the quality loss is invisible and the size
    /// difference is not.
    /// </param>
    public static byte[] Normalize(byte[] encoded, bool preserveLossless = false)
    {
        if (encoded.Length is 0 or > MaxEncodedBytes)
            throw new InvalidDataException("Image exceeds encoded byte limit.");
        using var stream = new MemoryStream(encoded, writable: false);
        using var codec =
            SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid image.");
        var info = codec.Info;
        if (
            codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png)
            || info.Width is <= 0 or > MaxDimension
            || info.Height is <= 0 or > MaxDimension
            || (long)info.Width * info.Height > MaxPixels
            || codec.FrameCount > 1
        )
            throw new InvalidDataException("Unsupported image format or dimensions.");
        using var decoded = new SKBitmap(
            info.Width,
            info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul
        );
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Incomplete image.");
        var swapped =
            codec.EncodedOrigin
            is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;
        int width = swapped ? info.Height : info.Width,
            height = swapped ? info.Width : info.Height;
        var scale = Math.Min(1d, (double)OutputDimension / Math.Max(width, height));
        using var normalized = new SKBitmap(
            Math.Max(1, (int)(width * scale)),
            Math.Max(1, (int)(height * scale))
        );
        using (var canvas = new SKCanvas(normalized))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale((float)normalized.Width / width, (float)normalized.Height / height);
            // EXIF origins are applied in source-pixel coordinates before downsampling.
            switch (codec.EncodedOrigin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Translate(width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(width, height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Translate(0, height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(width, height);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(-90);
                    break;
            }
            using var image = SKImage.FromBitmap(decoded);
            canvas.DrawImage(
                image,
                SKRect.Create(info.Width, info.Height),
                new SKSamplingOptions(SKFilterMode.Linear)
            );
        }
        using var result = SKImage.FromBitmap(normalized);
        using var data =
            preserveLossless && codec.EncodedFormat == SKEncodedImageFormat.Png
                ? result.Encode(SKEncodedImageFormat.Png, 100)
                : result.Encode(SKEncodedImageFormat.Jpeg, 85);
        return data.ToArray();
    }
}
