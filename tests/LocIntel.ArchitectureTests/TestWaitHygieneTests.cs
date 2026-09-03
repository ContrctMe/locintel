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
    // the bound may be followed by a compound condition (`i < 50 && mail is
    // null`) - the first version required a bare `N;` and missed 37 waits
    [GeneratedRegex(@"for \(\s*var \w+ = 0;\s*\w+ [<!]=? \w*\d+\b")]
    private static partial Regex CountingLoop();

    /// <summary>
    /// Shrink-only ratchet over the waits that still use the loop shape,
    /// keyed by file. New files and any growth fail; an entry higher than
    /// reality also fails, so the list can only get shorter. Convert with
    /// ApiFixture.WaitUntilAsync / WaitForAsync and lower the number.
    /// </summary>
    private static readonly Dictionary<string, int> Remaining = new()
    {
        ["AccessLogPartitioningTests.cs"] = 1,
        ["ApiKeyTests.cs"] = 1,
        ["AuditExportTests.cs"] = 2,
        ["AuditTests.cs"] = 4,
        ["BillingTests.cs"] = 1,
        ["ContactTierTests.cs"] = 2,
        ["DeadLetterTests.cs"] = 3,
        ["DirectorySyncTests.cs"] = 2,
        ["FileTrashTests.cs"] = 1,
        ["ImpersonationTests.cs"] = 2,
        ["IngestTests.cs"] = 5,
        ["LifecycleTests.cs"] = 1,
        ["OrgClosureTests.cs"] = 2,
        ["RateLimitTests.cs"] = 2,
        ["RotationTests.cs"] = 1,
        ["SiteClosureTests.cs"] = 3,
        ["SmtpTransportTests.cs"] = 1,
        ["StorageTests.cs"] = 1,
        ["WebhookTests.cs"] = 2,
    };

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

        var perFile = offenders
            .GroupBy(o => Path.GetFileName(o.Split(':')[0]))
            .ToDictionary(g => g.Key, g => g.Count());
        var grew = perFile
            .Where(kv => kv.Value > Remaining.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key}: {kv.Value} (allowed {Remaining.GetValueOrDefault(kv.Key)})")
            .ToList();
        var stale = Remaining
            .Where(kv => perFile.GetValueOrDefault(kv.Key) < kv.Value)
            .Select(kv =>
                $"{kv.Key}: allowed {kv.Value}, actual {perFile.GetValueOrDefault(kv.Key)}"
            )
            .ToList();
        Assert.True(
            grew.Count == 0,
            "hand-rolled outbox waits - use ApiFixture.WaitUntilAsync/WaitForAsync, which "
                + "fails saying what it waited for instead of falling through:\n  "
                + string.Join("\n  ", grew)
                + "\nsites:\n  "
                + string.Join("\n  ", offenders)
        );
        Assert.True(
            stale.Count == 0,
            "the wait ratchet only shrinks - lower these entries to match:\n  "
                + string.Join("\n  ", stale)
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
