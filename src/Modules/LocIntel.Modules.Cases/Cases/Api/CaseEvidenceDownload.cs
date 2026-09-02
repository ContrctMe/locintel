namespace LocIntel.Modules.Cases.Cases.Api;

public sealed record CaseEvidenceDownload(
    string Url,
    int ExpiresInSeconds,
    string FileName,
    string ContentType
);
