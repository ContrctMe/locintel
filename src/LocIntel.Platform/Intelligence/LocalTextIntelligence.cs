using System.Text.RegularExpressions;

namespace LocIntel.Platform.Intelligence;

/// <summary>
/// Deterministic keyword heuristics: no vendor, no network, same answer
/// every time - what tests assert against and what a fork runs until it
/// configures a model. Deliberately modest; the Claude adapter is the real
/// thing.
/// </summary>
public sealed class LocalTextIntelligence : ITextIntelligence
{
    public string Provider => "local";

    private static readonly (string Category, string[] Cues)[] CategoryCues =
    [
        (
            "OrganizedRetailCrime",
            ["crew", "organized", "group of", "three subjects", "filled bags", "boosters", "orc"]
        ),
        ("Robbery", ["robbery", "at gunpoint", "weapon", "demanded", "held up", "knife", "gun"]),
        (
            "Burglary",
            [
                "burglary",
                "broke in",
                "break-in",
                "forced entry",
                "after hours",
                "smashed the window",
            ]
        ),
        (
            "InternalTheft",
            ["employee", "associate", "cashier", "sweethearting", "void", "till", "register short"]
        ),
        (
            "Fraud",
            ["fraud", "counterfeit", "return fraud", "chargeback", "fake", "scam", "gift card"]
        ),
        ("Assault", ["assault", "punched", "hit", "attacked", "injur", "fight"]),
        ("Threat", ["threat", "threatened", "harass", "intimidat"]),
        ("Vandalism", ["vandal", "graffiti", "damaged", "smashed", "broke the"]),
        ("Trespass", ["trespass", "loiter", "refused to leave", "banned", "no trespass"]),
        ("Safety", ["slip", "fall", "spill", "hazard", "fire", "injury", "medical"]),
        ("Theft", ["shoplift", "stole", "theft", "concealed", "walked out", "took"]),
    ];

    public Task<IncidentSuggestion> SuggestIncidentAsync(
        IncidentSuggestionInput input,
        CancellationToken ct = default
    )
    {
        var text = $"{input.Title}\n{input.Narrative}".ToLowerInvariant();
        var scored = CategoryCues
            .Where(c => input.Categories.Contains(c.Category))
            .Select(c => (c.Category, Score: c.Cues.Count(text.Contains)))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ToList();
        var category =
            scored.Count > 0 ? scored[0].Category
            : input.Categories.Contains("Other") ? "Other"
            : input.Categories[0];
        var confidence = scored.Count == 0 ? 0.2 : Math.Min(0.95, 0.5 + 0.15 * scored[0].Score);

        var severity = "Medium";
        if (Regex.IsMatch(text, @"gun|knife|weapon|assault|injur|robbery|held up|medical"))
            severity = "Critical";
        else if (Regex.IsMatch(text, @"crew|organized|threat|broke in|burglary|\$?\s?[1-9]\d{3,}"))
            severity = "High";
        else if (Regex.IsMatch(text, @"minor|small|candy|single item|\$\s?[1-9]\d?(\.\d+)?\b"))
            severity = "Low";
        if (!input.Severities.Contains(severity))
            severity = input.Severities[Math.Min(1, input.Severities.Count - 1)];

        var tags = CategoryCues
            .SelectMany(c => c.Cues)
            .Where(cue => cue.Length > 3 && text.Contains(cue) && !cue.Contains(' '))
            .Distinct()
            .Take(5)
            .ToList();
        var firstSentence =
            Regex.Split(input.Narrative.Trim(), @"(?<=[.!?])\s+").FirstOrDefault(s => s.Length > 0)
            ?? input.Title;
        var summary = firstSentence.Length > 160 ? firstSentence[..157] + "..." : firstSentence;
        return Task.FromResult(
            new IncidentSuggestion(category, severity, tags, summary, confidence)
        );
    }

    public Task<string> DraftCaseBriefAsync(CaseBriefInput input, CancellationToken ct = default)
    {
        var lines = new List<string> { $"CASE BRIEF: {input.Title}", "" };
        if (input.Summary.Length > 0)
            lines.AddRange([input.Summary, ""]);
        lines.Add($"Incidents ({input.Incidents.Count}):");
        lines.AddRange(input.Incidents.Select(i => $"  - {i}"));
        lines.Add($"Persons and vehicles ({input.Entities.Count}):");
        lines.AddRange(input.Entities.Select(e => $"  - {e}"));
        lines.Add($"Evidence ({input.Evidence.Count}):");
        lines.AddRange(input.Evidence.Select(e => $"  - {e}"));
        if (input.Notes.Count > 0)
        {
            lines.Add("Investigator notes:");
            lines.AddRange(input.Notes.Select(n => $"  - {n}"));
        }
        lines.AddRange(["", "Drafted by the local heuristic; review before use."]);
        return Task.FromResult(string.Join('\n', lines));
    }
}
