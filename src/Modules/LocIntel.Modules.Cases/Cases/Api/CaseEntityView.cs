namespace LocIntel.Modules.Cases.Cases.Api;

/// <summary>Restricted rows are entities on the case the READER may not see: id and note withheld.</summary>
public sealed record CaseEntityView(
    Guid Id,
    Guid? EntityId,
    string? DisplayName,
    string? Kind,
    string? Status,
    string? Note,
    bool Restricted,
    DateTimeOffset AddedAt
);
