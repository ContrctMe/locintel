using System.Text.RegularExpressions;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// The application sets no session-scoped state on its database connections
/// (ADR 53): the tenant variable is <c>SET LOCAL</c>, and nothing else is SET
/// at all. Behind a transaction-mode pooler a server connection moves between
/// clients between transactions, so session state set by one client is read
/// by the next - for the tenant variable that is another tenant's rows. The
/// fleet suite proved it (three of five cases failed in transaction mode
/// before this rule); this keeps the property where a reviewer can see it.
/// Migrations and the migrate role run on direct owner connections and are
/// exempt; so is the frozen helper history.
/// </summary>
public partial class SessionStateTests
{
    [GeneratedRegex(
        @"set_config\s*\([^)]*,\s*false\s*\)|\bSET\s+(?:SESSION\s+)?(?!LOCAL\b)[a-z_]+\.[a-z_]+\s*=",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex SessionScopedSet();

    [Fact]
    public void Database_state_is_transaction_scoped_never_session_scoped()
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
                || relative.Contains(
                    $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"
                )
                || relative.EndsWith("MigrationRunner.cs", StringComparison.Ordinal)
                || relative.EndsWith("FrozenMigrationHelpers.cs", StringComparison.Ordinal)
                || relative.EndsWith("RlsMigrationExtensions.cs", StringComparison.Ordinal)
            )
                continue;
            var text = File.ReadAllText(file);
            foreach (Match m in SessionScopedSet().Matches(text))
            {
                // a comment describing the old shape is not a call
                var lineStart = text.LastIndexOf('\n', m.Index) + 1;
                if (text[lineStart..m.Index].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;
                offenders.Add($"{relative}:{text[..m.Index].Count(c => c == '\n') + 1}");
            }
        }
        Assert.True(
            offenders.Count == 0,
            "session-scoped database state breaks transaction pooling (ADR 53) - use SET LOCAL / set_config(..., true):\n  "
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
