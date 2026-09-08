using System.Text.RegularExpressions;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// A refused request has one body (ApiErrors: error + traceId). Before the
/// helper, a hundred endpoints spelled <c>new { error = "…" }</c> by hand and
/// none carried the trace id the 500 path and the gates already did, so
/// support could quote a trace for some failures and not others. The two
/// places that add fields of their own (GateResults, the unhandled-error
/// middleware) are the allowlist. Source scan, because the property is
/// about what a human can type.
/// </summary>
public partial class ErrorShapeTests
{
    [GeneratedRegex(@"new\s*\{\s*error\s*=")]
    private static partial Regex HandBuiltError();

    private static readonly string[] Allowed =
    [
        Path.Combine("src", "LocIntel.Contracts", "GateResults.cs"),
        Path.Combine("src", "LocIntel.Contracts", "ApiErrors.cs"),
        Path.Combine("src", "LocIntel.Api", "UnhandledErrorMiddleware.cs"),
    ];

    [Fact]
    public void Refusals_are_built_by_ApiErrors_not_by_hand()
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
            var relative = Path.GetRelativePath(root, file);
            if (
                relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || Allowed.Contains(relative)
            )
                continue;
            var text = File.ReadAllText(file);
            foreach (Match m in HandBuiltError().Matches(text))
                offenders.Add($"{relative}:{text[..m.Index].Count(c => c == '\n') + 1}");
        }
        Assert.True(
            offenders.Count == 0,
            "a refusal is ApiErrors.BadRequest / NotFound / Conflict(message) - one shape, with the trace id:\n  "
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
