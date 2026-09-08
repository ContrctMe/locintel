using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace LocIntel.Modules.Reporting.Rendering;

/// <summary>Deployment-owned XYZ raster providers. Tenant/browser URLs never enter this client.</summary>
public sealed class ReportBasemaps : IDisposable
{
    private readonly Dictionary<string, Provider> providers;
    private readonly HttpClient client;

    public ReportBasemaps(IConfiguration configuration, IHttpClientFactory clients)
    {
        providers = configuration
            .GetSection("Reports:Basemaps")
            .GetChildren()
            .Select(section =>
            {
                var provider = new Provider(
                    section.Key,
                    section["Url"] ?? "",
                    section["Attribution"] ?? ""
                );
                if (
                    provider.Id.Length > 80
                    || provider.Attribution.Length is 0 or > 500
                    || !provider.Url.Contains("{z}")
                    || !provider.Url.Contains("{x}")
                    || !provider.Url.Contains("{y}")
                )
                    throw new InvalidOperationException(
                        "Report basemap requires an ID, XYZ URL and attribution."
                    );
                var uri = provider.Tile(0, 0, 0);
                if (
                    uri.Scheme != "https"
                    || !uri.IsDefaultPort
                    || uri.UserInfo.Length != 0
                    || uri.Fragment.Length != 0
                    || Uri.CheckHostName(uri.Host) != UriHostNameType.Dns
                    || provider.Tile(1, 2, 3).Authority != uri.Authority
                )
                    throw new InvalidOperationException(
                        "Report basemap must use a public HTTPS DNS host on port 443."
                    );
                return provider;
            })
            .ToDictionary(p => p.Id, StringComparer.Ordinal);
        client = clients.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LocIntel-Reports/1.0");
    }

    public const string ClientName = "report-basemaps";

    public static SocketsHttpHandler CreateHandler() =>
        LocIntel.Platform.Http.PublicHttp.CreateHandler();

    public IReadOnlyCollection<Provider> All => providers.Values;

    public Provider? Find(string? id) => id is null ? null : providers.GetValueOrDefault(id);

    public async Task<byte[]> ReadTileAsync(
        Provider provider,
        int zoom,
        int x,
        int y,
        CancellationToken ct
    )
    {
        if (!providers.TryGetValue(provider.Id, out var configured) || configured != provider)
            throw new InvalidOperationException("Unknown report basemap.");
        using var response = await client.GetAsync(
            provider.Tile(zoom, x, y),
            HttpCompletionOption.ResponseHeadersRead,
            ct
        );
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > ReportImages.MaxEncodedBytes)
            throw new InvalidDataException("Map tile exceeds byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await ReportImages.ReadAsync(stream, ct, preserveLossless: true);
    }

    public static bool IsPublic(IPAddress address) =>
        LocIntel.Platform.Http.PublicHttp.IsPublic(address);

    public void Dispose() => client.Dispose();

    public sealed record Provider(string Id, string Url, string Attribution)
    {
        public Uri Tile(int z, int x, int y) =>
            new(
                Url.Replace("{z}", z.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{x}", x.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Replace("{y}", y.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                UriKind.Absolute
            );
    }
}
