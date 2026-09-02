namespace LocIntel.Contracts.Storage;

/// <summary>
/// Cross-module read (ADR 17/19): a short-lived download URL for a Clean
/// file, signed by Storage after the CALLER has authorized the read. Null
/// when the file is absent, not clean, or another org's. Callers that hold
/// evidence record the custody event themselves.
/// </summary>
public interface ISignedFileAccess
{
    Task<SignedFileUrl?> GetDownloadUrlAsync(Guid fileId, CancellationToken ct = default);
}

public sealed record SignedFileUrl(
    string Url,
    int ExpiresInSeconds,
    string FileName,
    string ContentType
);
