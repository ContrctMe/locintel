using LocIntel.Contracts;
using LocIntel.Platform.Spatial;
using SkiaSharp;

namespace LocIntel.Modules.Reporting.Rendering;

public sealed class ReportMap(ReportBasemaps basemaps)
{
    public const int Width = 768;
    public const int Height = 512;

    public async Task<byte[]> RenderAsync(
        ReportBasemaps.Provider provider,
        int maximumZoom,
        IReadOnlyList<ISiteSource.Site> sites,
        IReadOnlyList<IReportOverlaySource.Layer> overlays,
        CancellationToken ct
    )
    {
        var points = sites
            .Where(s => s.Latitude.HasValue && s.Longitude.HasValue)
            .Select(s => Project(s.Latitude!.Value, s.Longitude!.Value))
            .ToArray();
        if (points.Length == 0)
            throw new InvalidDataException("No site coordinates available.");
        var zoom = maximumZoom;
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        while (
            zoom > 0
            && (
                (maxX - minX) * (256 << zoom) > Width - 64
                || (maxY - minY) * (256 << zoom) > Height - 64
            )
        )
            zoom--;
        double world = 256 << zoom;
        var left = (minX + maxX) / 2 * world - Width / 2d;
        var top = (minY + maxY) / 2 * world - Height / 2d;
        using var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var n = 1 << zoom;
        for (
            int y = (int)Math.Floor(top / 256);
            y <= (int)Math.Floor((top + Height - 1) / 256);
            y++
        )
        for (
            int x = (int)Math.Floor(left / 256);
            x <= (int)Math.Floor((left + Width - 1) / 256);
            x++
        )
        {
            ct.ThrowIfCancellationRequested();
            if (y < 0 || y >= n)
                continue;
            var bytes = await basemaps.ReadTileAsync(provider, zoom, (x % n + n) % n, y, ct);
            using var tile = SKImage.FromEncodedData(bytes);
            canvas.DrawImage(
                tile,
                new SKRect(
                    (float)(x * 256 - left),
                    (float)(y * 256 - top),
                    (float)((x + 1) * 256 - left),
                    (float)((y + 1) * 256 - top)
                )
            );
        }
        DrawFeatures(canvas, overlays, points, world, left, top, ct);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    public static void DrawFeatures(
        SKCanvas canvas,
        IReadOnlyList<IReportOverlaySource.Layer> overlays,
        IReadOnlyList<(double X, double Y)> points,
        double world,
        double left,
        double top,
        CancellationToken ct
    )
    {
        using var fill = new SKPaint
        {
            Color = new SKColor(112, 70, 170, 65),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var stroke = new SKPaint
        {
            Color = new SKColor(92, 50, 135),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            IsAntialias = true,
        };
        foreach (var layer in overlays)
        foreach (var polygon in layer.Polygons)
        {
            ct.ThrowIfCancellationRequested();
            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            foreach (var ring in polygon)
            {
                for (var i = 0; i < ring.Length; i++)
                {
                    var point = Project(ring[i][1], ring[i][0]);
                    var pixel = new SKPoint(
                        (float)(point.X * world - left),
                        (float)(point.Y * world - top)
                    );
                    if (i == 0)
                        path.MoveTo(pixel);
                    else
                        path.LineTo(pixel);
                }
                path.Close();
            }
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, stroke);
        }
        fill.Color = SKColors.Crimson;
        stroke.Color = SKColors.White;
        foreach (var point in points)
        {
            var pixel = new SKPoint(
                (float)(point.X * world - left),
                (float)(point.Y * world - top)
            );
            canvas.DrawCircle(pixel, 6, fill);
            canvas.DrawCircle(pixel, 6, stroke);
        }
    }

    public static (double X, double Y) Project(double latitude, double longitude)
    {
        if (
            !double.IsFinite(latitude)
            || !double.IsFinite(longitude)
            || Math.Abs(latitude) > 90
            || Math.Abs(longitude) > 180
        )
            throw new InvalidDataException("Invalid map coordinates.");
        var rad =
            Math.Clamp(latitude, -SpatialCells.MaxLatitude, SpatialCells.MaxLatitude)
            * Math.PI
            / 180;
        return (
            (longitude + 180) / 360,
            (1 - Math.Log(Math.Tan(rad) + 1 / Math.Cos(rad)) / Math.PI) / 2
        );
    }
}
