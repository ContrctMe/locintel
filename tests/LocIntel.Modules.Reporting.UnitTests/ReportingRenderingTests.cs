using LocIntel.Modules.Reporting.Rendering;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace LocIntel.Modules.Reporting.UnitTests;

public sealed class ReportingRenderingTests
{
    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("2002:7f00:1::1", false)]
    [InlineData("2001::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Map_connections_reject_private_and_transition_addresses(
        string address,
        bool allowed
    ) => Assert.Equal(allowed, ReportBasemaps.IsPublic(System.Net.IPAddress.Parse(address)));

    [Fact]
    public void Overlay_polygon_holes_leave_the_basemap_visible()
    {
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        var layer = new LocIntel.Contracts.IReportOverlaySource.Layer(
            Guid.NewGuid(),
            "Hole",
            null,
            [
                [
                    [
                        [-90, -60],
                        [90, -60],
                        [90, 60],
                        [-90, 60],
                        [-90, -60],
                    ],
                    [
                        [-20, -20],
                        [20, -20],
                        [20, 20],
                        [-20, 20],
                        [-20, -20],
                    ],
                ],
            ]
        );
        ReportMap.DrawFeatures(canvas, [layer], [], 256, 0, 0, CancellationToken.None);
        Assert.Equal(SKColors.White, bitmap.GetPixel(128, 128));
        Assert.NotEqual(SKColors.White, bitmap.GetPixel(80, 128));
        ReportMap.DrawFeatures(
            canvas,
            [],
            [ReportMap.Project(0, 0)],
            256,
            0,
            0,
            CancellationToken.None
        );
        Assert.Equal(SKColors.Crimson, bitmap.GetPixel(128, 128));
    }

    [Fact]
    public void Bundled_fonts_and_normalized_image_produce_a_real_pdf()
    {
        ReportFonts.Configure();
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = ReportFonts.Family;
        var section = document.AddSection();
        section.AddParagraph("Montréal / São Paulo / façade", StyleNames.Heading1);
        var image = section.AddImage(
            "base64:" + Convert.ToBase64String(ReportImages.Normalize(Picture(40, 20)))
        );
        image.Width = Unit.FromCentimeter(10);
        using var output = new MemoryStream();
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(output, closeStream: false);
        renderer.PdfDocument.Dispose();
        output.Position = 0;
        using var reopened = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.PageCount);
        Assert.True(output.Length > 5000);
    }

    [Fact]
    public async Task Images_reject_corruption_and_limits_before_unbounded_allocation()
    {
        Assert.Throws<InvalidDataException>(() => ReportImages.Normalize([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() =>
            ReportImages.Normalize(new byte[ReportImages.MaxEncodedBytes + 1])
        );
        Assert.Throws<InvalidDataException>(() => ReportImages.Normalize(Picture(10001, 1)));
        Assert.Throws<InvalidDataException>(() => ReportImages.Normalize(Picture(5000, 4001)));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReportImages.ReadAsync(
                new MemoryStream(new byte[ReportImages.MaxEncodedBytes + 1]),
                CancellationToken.None
            )
        );
        using var image = SKBitmap.Decode(ReportImages.Normalize(Picture(3200, 800)));
        Assert.Equal(1600, image.Width);
        Assert.Equal(400, image.Height);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 2)]
    [InlineData(5, 0)]
    [InlineData(6, 2)]
    [InlineData(7, 3)]
    [InlineData(8, 1)]
    public void Photograph_exif_orientation_is_applied(int origin, int topLeftQuadrant)
    {
        using var source = new SKBitmap(80, 40);
        using (var canvas = new SKCanvas(source))
        using (var paint = new SKPaint())
        {
            SKColor[] colors = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow];
            for (int i = 0; i < 4; i++)
            {
                paint.Color = colors[i];
                canvas.DrawRect(i % 2 * 40, i / 2 * 20, 40, 20, paint);
            }
        }
        using var picture = SKImage.FromBitmap(source);
        using var jpeg = picture.Encode(SKEncodedImageFormat.Jpeg, 100);
        var bytes = jpeg.ToArray();
        // A single TIFF orientation tag in a JPEG APP1 fixture; no production EXIF parser.
        byte[] exif =
        [
            0xff,
            0xe1,
            0,
            34,
            69,
            120,
            105,
            102,
            0,
            0,
            73,
            73,
            42,
            0,
            8,
            0,
            0,
            0,
            1,
            0,
            18,
            1,
            3,
            0,
            1,
            0,
            0,
            0,
            (byte)origin,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
        ];
        using var normalized = SKBitmap.Decode(
            ReportImages.Normalize([.. bytes[..2], .. exif, .. bytes[2..]])
        );
        Assert.Equal(origin >= 5 ? 40 : 80, normalized.Width);
        Assert.Equal(origin >= 5 ? 80 : 40, normalized.Height);
        SKColor[] expected = [SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.Yellow];
        var actual = normalized.GetPixel(10, 10);
        Assert.InRange(Math.Abs(actual.Red - expected[topLeftQuadrant].Red), 0, 15);
        Assert.InRange(Math.Abs(actual.Green - expected[topLeftQuadrant].Green), 0, 15);
        Assert.InRange(Math.Abs(actual.Blue - expected[topLeftQuadrant].Blue), 0, 15);
    }

    private static byte[] Picture(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
