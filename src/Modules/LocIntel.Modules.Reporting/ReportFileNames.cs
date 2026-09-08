using System.Text.RegularExpressions;

namespace LocIntel.Modules.Reporting;

/// <summary>
/// Human file names for generated output. A run's PDFs become site files, and
/// "report-0199a07e....pdf" tells a reader nothing about which site it covers.
/// Names are display text, never identity: the artifact id stays the key, so a
/// renamed or duplicate-named site cannot collide with anything that matters.
/// Dates are the UTC generation date, matching the stamped generation instant.
/// </summary>
public static partial class ReportFileNames
{
    private const int MaxSiteName = 60;

    public static string Pdf(
        Guid[] siteIds,
        IReadOnlyDictionary<Guid, string> names,
        DateTimeOffset generatedAt
    )
    {
        var label =
            siteIds.Length == 1 && names.TryGetValue(siteIds[0], out var only)
                ? Safe(only)
                : $"{siteIds.Length} sites";
        return $"{label} report {generatedAt:yyyy-MM-dd}.pdf";
    }

    public static string Zip(int sites, DateTimeOffset completedAt) =>
        $"{sites} site reports {completedAt:yyyy-MM-dd}.zip";

    /// <summary>
    /// A unique entry name within one archive: two sites may legitimately share
    /// a name, and an archive with two identical entries extracts ambiguously.
    /// </summary>
    public static string Unique(HashSet<string> taken, string name)
    {
        if (taken.Add(name))
            return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){extension}";
            if (taken.Add(candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Site names are tenant input. Strip anything that could traverse a path or
    /// break an archive entry, collapse whitespace, then bound the length so the
    /// composed name always fits the artifact name column.
    /// </summary>
    private static string Safe(string name)
    {
        var stripped = new string([
            .. name.Select(c => char.IsControl(c) || """/\:*?"<>|""".Contains(c) ? ' ' : c),
        ]);
        var collapsed = Whitespace().Replace(stripped, " ").Trim(' ', '.');
        if (collapsed.Length == 0)
            return "Site";
        return collapsed.Length <= MaxSiteName ? collapsed : collapsed[..MaxSiteName].TrimEnd();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
