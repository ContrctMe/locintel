namespace LocIntel.Contracts.Entities;

/// <summary>What a module above Entities may know about a record the CURRENT principal may see.</summary>
public sealed record EntityInfo(
    Guid Id,
    string Kind,
    string Status,
    string DisplayName,
    string[] Aliases,
    IReadOnlyDictionary<string, string> Descriptors
);
