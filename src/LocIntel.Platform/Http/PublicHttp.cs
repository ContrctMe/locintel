using System.Net;
using System.Net.Sockets;

namespace LocIntel.Platform.Http;

/// <summary>Tenant-supplied destinations must stay public at the actual TCP connection.</summary>
public static class PublicHttp
{
    public static bool IsAllowedUrl(string? value, bool allowLoopback = false)
    {
        if (
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0
            || value is { Length: > 1000 }
        )
            return false;
        if (allowLoopback && uri.IsLoopback)
            return uri.Scheme is "http" or "https";
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.IsLoopback)
            return false;
        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
            ? IsPublic(address)
            : Uri.CheckHostName(uri.Host) == UriHostNameType.Dns;
    }

    public static SocketsHttpHandler CreateHandler(bool allowLoopback = false) =>
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            MaxResponseDrainSize = 0,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, ct) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                if (
                    addresses.Length == 0
                    || addresses.Any(a =>
                        !IsPublic(a) && !(allowLoopback && IPAddress.IsLoopback(a))
                    )
                )
                    throw new HttpRequestException(
                        "Remote endpoint resolved to a prohibited address."
                    );
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    // Connect to the validated addresses, avoiding a second DNS resolution/rebinding gap.
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var b = address.GetAddressBytes();
        if (b.Length == 16)
            return (b[0] & 0xe0) == 0x20 // global unicast only; excludes local/mapped/multicast/NAT64
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] < 2) // protocol assignments, including Teredo
                && !(b[0] == 0x20 && b[1] == 0x02) // 6to4 can encapsulate a private IPv4 destination
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8);
        return b[0] is not (0 or 10 or 127)
            && b[0] < 224
            && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            && !(b[0] == 169 && b[1] == 254)
            && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2))
            && !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100))
            && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
}
