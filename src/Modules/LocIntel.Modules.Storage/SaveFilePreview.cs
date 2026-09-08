namespace LocIntel.Modules.Storage;

public sealed record SaveFilePreview(Guid FileId, string Key, byte[] Content);
