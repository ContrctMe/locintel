namespace LocIntel.Contracts.Entities;

/// <summary>
/// Cross-module WRITE (ADR 17): a shared intelligence bulletin is copied
/// into the receiving org's own entities. Entities applies it over the
/// outbox with the tenant on the envelope; the new record starts Suspected
/// and carries where it came from in its summary.
/// </summary>
public sealed record ImportEntityRequested(
    string Kind,
    string DisplayName,
    string[] Aliases,
    IReadOnlyDictionary<string, string> Descriptors,
    string Summary,
    Guid RequestedBy
);
