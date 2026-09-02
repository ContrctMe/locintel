namespace LocIntel.Platform.Storage;

/// <summary>
/// Object storage port (ADR 19): TICKETS, not streams - the browser talks to
/// storage directly and bytes never proxy through the API. S3 presigned
/// PUT/POST and Azure SAS both fit this shape; the local adapter serves the
/// same contract for dev and tests. Server-side reads (scanning, ingest
/// parsing, derivatives) use OpenRead/Write - those are backend-to-storage,
/// not client traffic.
/// </summary>
public interface IObjectStore
{
    /// <summary>Short-lived instruction for the CLIENT to upload directly to storage.</summary>
    ValueTask<UploadTicket> CreateUploadTicketAsync(
        string key,
        string contentType,
        long maxBytes,
        CancellationToken ct = default
    );

    /// <summary>Short-TTL download URL. Authorization happens BEFORE signing; the URL itself is unguarded.</summary>
    ValueTask<Uri> GetDownloadUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    ValueTask<bool> ExistsAsync(string key, CancellationToken ct = default);
    ValueTask<Stream> OpenReadAsync(string key, CancellationToken ct = default);
    ValueTask WriteAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken ct = default
    );
    ValueTask DeleteAsync(string key, CancellationToken ct = default);
}

public sealed record UploadTicket(
    string Url,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset ExpiresAt
);

/// <summary>
/// Scan hook (ADR 19): uploads are quarantined until a verdict. The local
/// scanner flags the EICAR test string; production forks plug ClamAV or a
/// cloud scanning service in here.
/// </summary>
public interface IVirusScanner
{
    ValueTask<ScanVerdict> ScanAsync(Stream content, CancellationToken ct = default);
}

public enum ScanVerdict
{
    Clean,
    Infected,
}
