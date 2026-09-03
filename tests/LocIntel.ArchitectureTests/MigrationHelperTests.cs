using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// A helper called from a migration is part of that migration's frozen text.
/// Applied migrations are immutable, so the helper they compile against must
/// be too: ADR 48 deleted the multi-owner shape helpers and every fork
/// migration that used them stopped compiling (template feedback, round
/// five, item 19). The compiler catches the deletion in the FORK, after the
/// sync; this catches it in the template, before.
///
/// Two rules. A helper that ever shipped still exists (removal is a freeze,
/// never a delete - see FrozenMigrationHelpers). And a frozen helper is not
/// called from a migration stamped after its freeze date: frozen means
/// "keeps old migrations compiling", not "still a shape you may use".
/// </summary>
public partial class MigrationHelperTests
{
    /// <summary>
    /// Every public MigrationBuilder extension Platform has ever shipped.
    /// One you add today is on this list tomorrow, because a fork's migration
    /// may call it by then. Removing an entry is what this test refuses.
    /// </summary>
    private static readonly string[] EverShipped =
    [
        "EnableTenantRls",
        "EnableTwoPartyRls",
        "EnablePublishedCatalogRls",
        "EnableRecipientListRls",
    ];

    /// <summary>Frozen helper -> the migration timestamp after which calling it is a new use.</summary>
    private static readonly Dictionary<string, string> FrozenAt = new()
    {
        // ADR 48 removed the multi-owner shapes. The stamp is when the ADR LANDED
        // in this fork (the fourth sync, 2026-09-03), not the day's midnight: the
        // fork's three shape migrations are stamped 2026-09-02, when the helpers
        // were still the template's recommended shape (feedback, round six).
        ["EnableTwoPartyRls"] = "20260903000000",
        ["EnablePublishedCatalogRls"] = "20260903000000",
        ["EnableRecipientListRls"] = "20260903000000",
    };

    [GeneratedRegex(@"^(\d{14})_")]
    private static partial Regex MigrationStamp();

    [Fact]
    public void A_migration_helper_that_ever_shipped_still_exists()
    {
        var shipped = typeof(LocIntel.Platform.Data.RlsMigrationExtensions)
            .Assembly.GetTypes()
            .Where(t => t is { IsAbstract: true, IsSealed: true, IsPublic: true }) // static classes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m =>
                m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false)
                && m.GetParameters()[0].ParameterType == typeof(MigrationBuilder)
            )
            .Select(m => m.Name)
            .ToHashSet();

        var removed = EverShipped.Except(shipped).Order().ToArray();
        Assert.True(
            removed.Length == 0,
            "migration helpers removed from Platform - a helper called from a migration is part "
                + "of that migration's frozen text, and applied migrations are never edited. Move it "
                + "to FrozenMigrationHelpers with its signature and SQL unchanged (not [Obsolete]: "
                + "warnings-as-errors would break the very migrations the freeze protects): "
                + string.Join(", ", removed)
        );

        var unlisted = shipped.Except(EverShipped).Order().ToArray();
        Assert.True(
            unlisted.Length == 0,
            "new migration helpers - add them to EverShipped, because from now on a fork's "
                + "migration may call them and they can only ever be frozen, never removed: "
                + string.Join(", ", unlisted)
        );
    }

    [Fact]
    public void A_frozen_helper_is_not_called_from_a_migration_written_after_its_freeze()
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
                !file.Contains(
                    $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"
                )
                || file.EndsWith(".Designer.cs", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
                continue;
            var stamp = MigrationStamp().Match(Path.GetFileName(file));
            if (!stamp.Success)
                continue; // a snapshot or a hand-written support file, not a migration
            var text = File.ReadAllText(file);
            foreach (var (helper, frozenAt) in FrozenAt)
            {
                if (
                    string.CompareOrdinal(stamp.Groups[1].Value, frozenAt) > 0
                    && text.Contains($".{helper}(", StringComparison.Ordinal)
                )
                    offenders.Add($"{Path.GetRelativePath(root, file)} calls {helper}");
            }
        }
        Assert.True(
            offenders.Count == 0,
            "frozen helpers keep OLD migrations compiling; they are not a shape a new migration "
                + "may use (ADR 48: EnableTenantRls is the only tenancy shape):\n  "
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
