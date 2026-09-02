namespace LocIntel.Platform.Intelligence;

/// <summary>
/// The AI assistance port (blueprint module 10). Adapters: a deterministic
/// keyword heuristic ("local", dev/test and the no-vendor default) and the
/// Claude adapter in LocIntel.Integrations.Anthropic. Every suggestion is
/// exactly that - a human applies it (the blueprint keeps a person in the
/// loop on anything touching a person record).
/// </summary>
public interface ITextIntelligence
{
    /// <summary>Adapter name surfaced to the client so the UI can say where a suggestion came from.</summary>
    string Provider { get; }

    /// <summary>Suggest category, severity, tags, and a one-line summary for an incident narrative.</summary>
    Task<IncidentSuggestion> SuggestIncidentAsync(
        IncidentSuggestionInput input,
        CancellationToken ct = default
    );

    /// <summary>Draft a case brief from the facts the case holds.</summary>
    Task<string> DraftCaseBriefAsync(CaseBriefInput input, CancellationToken ct = default);
}

/// <summary>What the classifier sees: the narrative plus the closed sets it must choose from.</summary>
public sealed record IncidentSuggestionInput(
    string Title,
    string Narrative,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Severities
);

public sealed record IncidentSuggestion(
    string Category,
    string Severity,
    IReadOnlyList<string> Tags,
    string Summary,
    double Confidence
);

/// <summary>The facts a case holds, already redacted of anything the reader may not see.</summary>
public sealed record CaseBriefInput(
    string Title,
    string Summary,
    IReadOnlyList<string> Incidents,
    IReadOnlyList<string> Entities,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Evidence
);
