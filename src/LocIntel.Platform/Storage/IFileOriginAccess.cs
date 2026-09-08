using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.Storage;

/// <summary>
/// Host-wired authorization extension for generated content. Storage owns the file
/// lifecycle; the producer rechecks captured dependencies. Unknown origins deny access.
/// </summary>
public interface IFileOriginAccess
{
    string Origin { get; }
    Task<bool> CanReadAsync(OrgId org, Guid fileId, Guid userId, CancellationToken ct);
}
