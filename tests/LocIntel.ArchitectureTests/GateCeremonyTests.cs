using System.Text.RegularExpressions;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// The three gates are one module (Gate + GateResults), not a ceremony each
/// endpoint re-derives. Before that module, 67 CanAsync sites answered a
/// missing grant with 401 where the contract specifies 403, in five textual
/// variants, and the reference slice was propagating a sixth. This keeps the
/// status mapping in one place by refusing the two hand-rolled shapes:
///
///  - a capability or operator check whose failure is answered inline with
///    Results.Unauthorized() (the 401-for-403 drift), and
///  - a hand-built 402 body outside GateResults (gate 1 had grown three).
///
/// Bare "is the caller signed in?" guards are fine - a 401 for no principal
/// is the contract - so only conditions that consult the resolver are caught.
/// Source scan, because the property is about what a human can type.
/// </summary>
public partial class GateCeremonyTests
{
    [GeneratedRegex(@"\.(CanAsync|IsOperatorAsync)\(")]
    private static partial Regex ResolverCall();

    [Fact]
    public void Grant_failures_are_never_answered_inline_with_401()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();
        foreach (var file in EndpointSources(root))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"(?m)^[ \t]*if \("))
            {
                var open = m.Index + m.Length - 1;
                var close = MatchingParen(text, open);
                if (close < 0)
                    continue;
                var condition = text[(open + 1)..close];
                if (!ResolverCall().IsMatch(condition))
                    continue;
                var after = text[(close + 1)..Math.Min(text.Length, close + 120)];
                if (Regex.IsMatch(after, @"^\s*\{?\s*return Results\.Unauthorized\(\)"))
                    offenders.Add(
                        $"{Path.GetRelativePath(root, file)}:{text[..m.Index].Count(c => c == '\n') + 1}"
                    );
            }
        }
        Assert.True(
            offenders.Count == 0,
            "a missing grant is 403, and the mapping lives in Gate/GateResults - use "
                + "Gate.RequireAsync / RequireUserAsync / RequireOperatorAsync and gate.ToResult():\n  "
                + string.Join("\n  ", offenders)
        );
    }

    [Fact]
    public void The_402_body_is_built_in_exactly_one_place()
    {
        var root = RepositoryRoot();
        var offenders = EndpointSources(root)
            .Where(f =>
                File.ReadAllText(f).Contains("Status402PaymentRequired", StringComparison.Ordinal)
            )
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();
        Assert.True(
            offenders.Count == 0,
            "gate 1 answers through GateResults.LimitReached / FeatureOff, never an inline body:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    private static IEnumerable<string> EndpointSources(string root) =>
        Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains(
                    $"{Path.DirectorySeparatorChar}LocIntel.Platform{Path.DirectorySeparatorChar}"
                )
                && !f.Contains(
                    $"{Path.DirectorySeparatorChar}LocIntel.Contracts{Path.DirectorySeparatorChar}"
                )
            );

    private static int MatchingParen(string text, int open)
    {
        var depth = 0;
        var inString = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }
            if (c == '"')
                inString = true;
            else if (c == '(')
                depth++;
            else if (c == ')' && --depth == 0)
                return i;
        }
        return -1;
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
