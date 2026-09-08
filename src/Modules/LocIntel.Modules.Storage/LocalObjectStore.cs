using System.Security.Cryptography;
using LocIntel.Platform.Storage;
using Microsoft.Extensions.Configuration;

namespace LocIntel.Modules.Storage;

/// <summary>
/// Filesystem adapter for dev/tests: honors the SAME ticket contract as the
/// cloud adapters - clients PUT to a tokenized URL served by this API (see
/// LocalStoreEndpoints), so the whole ticket flow is exercised in-process.
/// </summary>
public sealed class LocalObjectStore(IConfiguration configuration) : IObjectStore
{
    private readonly string _root =
        configuration["Storage:LocalRoot"] ?? Path.Combine(Path.GetTempPath(), "locintel-objects");

    // token -> (key, expiry); tickets are short-lived by design
    private readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        (string key, long maxBytes, DateTimeOffset expires)
    > _tickets = new();

    public ValueTask<UploadTicket> CreateUploadTicketAsync(
        string key,
        string contentType,
        long maxBytes,
        CancellationToken ct = default
    )
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        _tickets[token] = (key, maxBytes, expires);
        return ValueTask.FromResult(
            new UploadTicket(
                $"/objects/upload/{token}",
                "PUT",
                new Dictionary<string, string> { ["Content-Type"] = contentType },
                expires
            )
        );
    }

    public ValueTask<Uri> GetDownloadUrlAsync(
        string key,
        TimeSpan ttl,
        CancellationToken ct = default
    )
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _tickets[token] = (key, 0, DateTimeOffset.UtcNow.Add(ttl));
        return ValueTask.FromResult(new Uri($"/objects/download/{token}", UriKind.Relative));
    }

    public (string key, long maxBytes)? Redeem(string token)
    {
        if (!_tickets.TryRemove(token, out var ticket) || ticket.expires < DateTimeOffset.UtcNow)
            return null;
        return (ticket.key, ticket.maxBytes);
    }

    public ValueTask<long?> GetLengthAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return ValueTask.FromResult<long?>(new FileInfo(PathFor(key)).Length);
        }
        catch (FileNotFoundException)
        {
            return ValueTask.FromResult<long?>(null);
        }
        catch (DirectoryNotFoundException)
        {
            return ValueTask.FromResult<long?>(null);
        }
    }

    public ValueTask<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
        ValueTask.FromResult<Stream>(File.OpenRead(PathFor(key)));

    public async ValueTask WriteAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken ct = default
    )
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
    }

    public ValueTask DeleteAsync(string key, CancellationToken ct = default)
    {
        File.Delete(PathFor(key));
        return ValueTask.CompletedTask;
    }

    public string PathFor(string key)
    {
        // keys are server-generated, but never trust a path join
        var path = Path.GetFullPath(
            Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar))
        );
        return path.StartsWith(Path.GetFullPath(_root))
            ? path
            : throw new InvalidOperationException("key escapes the storage root");
    }
}

/// <summary>EICAR-detecting scanner for dev/tests (ADR 19); forks plug real scanning here.</summary>
public sealed class EicarScanner : IVirusScanner
{
    private const string Eicar =
        @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public async ValueTask<ScanVerdict> ScanAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, leaveOpen: false);
        var buffer = new char[128 * 1024];
        var read = await reader.ReadBlockAsync(buffer, ct);
        return new string(buffer, 0, read).Contains(Eicar)
            ? ScanVerdict.Infected
            : ScanVerdict.Clean;
    }
}
