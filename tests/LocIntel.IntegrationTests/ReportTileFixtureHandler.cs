using System.Net;
using SkiaSharp;

namespace LocIntel.IntegrationTests;

/// <summary>Deterministic upstream fixture; the real report composition and database authorization still execute.</summary>
public sealed class ReportTileFixtureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        if (path.StartsWith("/unavailable/"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        if (path.StartsWith("/redirect/"))
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("http://127.0.0.1/private") },
                }
            );
        if (path.StartsWith("/corrupt/"))
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3]),
                }
            );
        using var bitmap = new SKBitmap(256, 256);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(238, 241, 234));
        using var paint = new SKPaint { Color = SKColors.White, StrokeWidth = 8 };
        canvas.DrawLine(0, 128, 256, 128, paint);
        canvas.DrawLine(128, 0, 128, 256, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(encoded.ToArray()),
            }
        );
    }
}
