using System.Text.RegularExpressions;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// Round-three item 17: hand-rolled outbox waits keep coming back. They are
/// `for (var i = 0; i < N; i++) { ...; await Task.Delay(100); }` with an
/// arbitrary N, and on timeout they fall through rather than fail - so the
/// symptom appears as an unrelated assert further down, or as a pass.
///
/// ApiFixture.WaitUntilAsync / WaitForAsync / WaitForMembershipAsync replace
/// them with one generous bound and a message naming the predicate. This
/// keeps the shape from returning.
///
/// It lives with the architecture tests rather than the product's
/// HygieneTests (where the item suggested it) because it is a source scan:
/// it needs no fixture and no Postgres, so it fails in the fast job.
/// </summary>
public partial class TestWaitHygieneTests
{
    [GeneratedRegex(@"for \(\s*var \w+ = 0;\s*\w+ [<!]=? \w*\d+;")]
    private static partial Regex CountingLoop();

    [Fact]
    public void Tests_do_not_hand_roll_outbox_waits()
    {
        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (
            var file in Directory.EnumerateFiles(
                Path.Combine(root, "tests"),
                "*.cs",
                SearchOption.AllDirectories
            )
        )
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!CountingLoop().IsMatch(lines[i]))
                    continue;
                // a delay inside the loop body is what makes it a WAIT rather
                // than a loop that legitimately does something N times
                var body = string.Join('\n', lines.Skip(i).Take(16));
                if (body.Contains("Task.Delay(", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "hand-rolled outbox waits - use ApiFixture.WaitUntilAsync/WaitForAsync, which "
                + "fails saying what it waited for instead of falling through:\n  "
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
