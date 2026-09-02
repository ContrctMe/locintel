using LocIntel.Platform.Kernel;

namespace LocIntel.Contracts;

/// <summary>
/// Resolve actor ids to a human label (email) for display - implemented by
/// Identity (users are platform-global), consumed by read surfaces that
/// stored only the id. Ids with no user (deleted accounts, system actors)
/// are simply absent from the result.
/// </summary>
public interface IActorDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> LabelsAsync(
        IReadOnlyCollection<Guid> actorIds,
        CancellationToken ct = default
    );
}
