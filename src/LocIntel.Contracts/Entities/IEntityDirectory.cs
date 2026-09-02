namespace LocIntel.Contracts.Entities;

/// <summary>
/// Entity lookup for modules above Entities (cases, marketplace). The
/// implementation applies the need-to-know gate for the CURRENT principal:
/// ids the caller may not see are simply absent. Callers never learn a
/// name they could not open the record of.
/// </summary>
public interface IEntityDirectory
{
    Task<IReadOnlyDictionary<Guid, EntityInfo>> LookupVisibleAsync(
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken ct = default
    );
}
