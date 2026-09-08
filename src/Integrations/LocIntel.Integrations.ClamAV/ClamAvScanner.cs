using System.Net.Sockets;
using System.Text;
using LocIntel.Platform.Storage;
using Microsoft.Extensions.Options;

namespace LocIntel.Integrations.ClamAV;

/// <summary>
/// The production malware scanner (ADR 19): streams the quarantined object to
/// a clamd daemon over TCP (INSTREAM) and returns its verdict. Fails closed -
/// a refused connection, a timeout or an unparseable reply is an exception,
/// so the object stays in quarantine and the scan is retried, never marked
/// clean by default. Forks with a commercial scanner implement IVirusScanner
/// the same way behind the same port.
/// </summary>
public sealed class ClamAvScanner(IOptions<ClamAvOptions> options) : IVirusScanner
{
    private readonly ClamAvOptions _options = options.Value;

    public async ValueTask<ScanVerdict> ScanAsync(Stream content, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var token = timeout.Token;

        using var client = new TcpClient();
        await client.ConnectAsync(_options.Host, _options.Port, token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(ClamAvProtocol.Command, token);

        var buffer = new byte[ClamAvProtocol.MaxChunk];
        int read;
        while ((read = await content.ReadAsync(buffer, token)) > 0)
            await stream.WriteAsync(ClamAvProtocol.Frame(buffer.AsSpan(0, read)), token);
        await stream.WriteAsync(ClamAvProtocol.End, token);

        var reply = new StringBuilder();
        var one = new byte[256];
        while ((read = await stream.ReadAsync(one, token)) > 0)
        {
            reply.Append(Encoding.ASCII.GetString(one, 0, read));
            if (Array.IndexOf(one, (byte)0, 0, read) >= 0)
                break;
        }
        return ClamAvProtocol.ParseVerdict(reply.ToString());
    }
}
