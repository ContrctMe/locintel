using LocIntel.Platform.Storage;

namespace LocIntel.Modules.Storage;

public sealed record ApplyFileScan(Guid FileId, string Key, ScanVerdict Verdict);
