namespace LocIntel.ArchitectureTests;

/// <summary>
/// Round-two item 12: AuditAsync retired the hand-written audit publishes,
/// but two endpoints kept spelling the envelope headers by hand and would
/// have quietly bred more. A mistyped header fails nothing - the record
/// simply lands unattributed, the quietest way to lose an audit trail - so
/// the literal is worth forbidding rather than reviewing.
///
/// Source scan rather than IL inspection: the point is that a HUMAN cannot
/// type the string anywhere but its one definition, which is a property of
/// the source, and the failure message can then name the file and line.
/// </summary>
public class AuditHeaderLiteralTests
{
    private static readonly string[] Literals = ["locintel-actor-tier", "locintel-actor-id"];
    private const string DefinitionFile = "AuditHeaders.cs";

    [Fact]
    public void The_audit_header_names_are_spelled_in_exactly_one_place()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(
                Path.Combine(root, "src"),
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            if (
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || Path.GetFileName(file) == DefinitionFile
            )
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (var literal in Literals)
                    if (lines[i].Contains($"\"{literal}\"", StringComparison.Ordinal))
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
        }

        Assert.True(
            offenders.Count == 0,
            $"audit header names must come from AuditHeaders (or bus.AuditAsync), never a "
                + $"literal - a typo there loses attribution silently:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
