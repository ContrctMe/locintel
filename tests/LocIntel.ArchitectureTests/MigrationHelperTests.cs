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

    /// <summary>
    /// Frozen helper -> the UTC migration stamp of the moment it froze, read
    /// from [FrozenAt] on the helper itself. A moment, not a day: a fork had
    /// three legitimate migrations from earlier on the day ADR 48 landed.
    /// </summary>
    private static Dictionary<string, string> FrozenAt =>
        typeof(LocIntel.Platform.Data.FrozenMigrationHelpers)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(
                m => m.Name,
                m =>
                    m.GetCustomAttribute<LocIntel.Platform.Data.FrozenAtAttribute>()?.MigrationStamp
                    ?? throw new Xunit.Sdk.XunitException(
                        $"{m.Name} is frozen without a [FrozenAt] moment - it cannot be policed"
                    )
            );

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
        var migrations = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                && !f.EndsWith(".Designer.cs", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Select(f => (Path.GetRelativePath(root, f), File.ReadAllText(f)));
        var offenders = NewUses(migrations, FrozenAt);
        Assert.True(
            offenders.Count == 0,
            "frozen helpers keep OLD migrations compiling; they are not a shape a new migration "
                + "may use (ADR 48: EnableTenantRls is the only tenancy shape):\n  "
                + string.Join("\n  ", offenders)
        );
    }

    // the fork's real stamps from the day ADR 48 landed (23:20:55Z): legitimate
    private const string ForkThatMorning = "20260902171347_Marketplace.cs";
    private const string ForkThatEvening = "20260902223221_Shares.cs";
    private const string Call = "migrationBuilder.EnableTwoPartyRls(\"s\", \"t\", \"c\");";

    [Fact]
    public void A_migration_stamped_earlier_on_the_day_of_the_freeze_is_not_a_new_use()
    {
        var uses = NewUses([(ForkThatMorning, Call), (ForkThatEvening, Call)], FrozenAt);

        Assert.Empty(uses);
    }

    [Fact]
    public void The_freeze_moment_itself_is_not_a_new_use_but_the_next_second_is()
    {
        var at = FrozenAt["EnableTwoPartyRls"];
        var after = (long.Parse(at) + 1).ToString();

        Assert.Empty(NewUses([($"{at}_AtTheMoment.cs", Call)], FrozenAt));
        Assert.Equal(
            [$"{after}_AfterTheMoment.cs calls EnableTwoPartyRls"],
            NewUses([($"{after}_AfterTheMoment.cs", Call)], FrozenAt)
        );
    }

    [Fact]
    public void A_snapshot_or_support_file_in_the_migrations_folder_is_not_a_migration()
    {
        // a fork that froze its own copy per module keeps it in Migrations/
        Assert.Empty(NewUses([("Migrations/LegacyTenancyShapes.cs", Call)], FrozenAt));
    }

    /// <summary>The pure decision: which migrations call a frozen helper after its moment.</summary>
    private static List<string> NewUses(
        IEnumerable<(string Path, string Text)> migrations,
        IReadOnlyDictionary<string, string> frozenAt
    )
    {
        var offenders = new List<string>();
        foreach (var (path, text) in migrations)
        {
            var stamp = MigrationStamp().Match(Path.GetFileName(path));
            if (!stamp.Success)
                continue; // a snapshot or a hand-written support file, not a migration
            foreach (var (helper, moment) in frozenAt)
            {
                if (
                    string.CompareOrdinal(stamp.Groups[1].Value, moment) > 0
                    && text.Contains($".{helper}(", StringComparison.Ordinal)
                )
                    offenders.Add($"{path} calls {helper}");
            }
        }
        return offenders;
    }

    /// <summary>
    /// A frozen helper's compatibility surface is its signature AND the SQL it
    /// emits: a fork's applied migration compiles against the first and was
    /// applied with the second. Both are snapshotted here verbatim; changing
    /// either is changing frozen text.
    /// </summary>
    [Fact]
    public void Frozen_helper_signatures_are_unchanged()
    {
        var signatures = typeof(LocIntel.Platform.Data.FrozenMigrationHelpers)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.ToString()!)
            .Order()
            .ToArray();
        Assert.Equal(
            [
                "Void EnablePublishedCatalogRls(Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder, System.String, System.String, System.String, System.String)",
                "Void EnableRecipientListRls(Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder, System.String, System.String, System.String, System.String, System.String, System.String, System.String, System.String, Boolean, System.String, System.String, System.String)",
                "Void EnableTwoPartyRls(Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder, System.String, System.String, System.String, System.String)",
            ],
            signatures
        );
    }

    [Fact]
    public void Frozen_helper_sql_is_unchanged()
    {
        Assert.Equal(
            """
            ALTER TABLE "s"."t" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "s"."t" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "s"."t"
                USING (
                    "org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid
                    OR "other_org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid
                )
                WITH CHECK (
                    "org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid
                    OR "other_org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid
                );
            """,
            LocIntel.Platform.Data.FrozenMigrationHelpers.TwoPartySql("s", "t", "other_org_id")
        );
        Assert.Equal(
            """
            ALTER TABLE "s"."t" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "s"."t" FORCE ROW LEVEL SECURITY;
            CREATE POLICY catalog_read ON "s"."t" FOR SELECT
                USING (
                    "published"
                    OR "org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid
                );
            CREATE POLICY owner_write ON "s"."t" FOR ALL
                USING ("org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid)
                WITH CHECK ("org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid);
            """,
            LocIntel.Platform.Data.FrozenMigrationHelpers.CatalogSql("s", "t")
        );
        Assert.Equal(
            """
            ALTER TABLE "s"."t" ENABLE ROW LEVEL SECURITY;
            ALTER TABLE "s"."t" FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON "s"."t"
                USING ("org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid OR EXISTS (SELECT 1 FROM "s"."t_recipients" r WHERE r."t_id" = "t"."id" AND r."recipient_org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid))
                WITH CHECK ("org_id" = NULLIF(current_setting('app.org_id', true), '')::uuid);
            """,
            LocIntel.Platform.Data.FrozenMigrationHelpers.RecipientListSql(
                "s",
                "t",
                "t_recipients",
                "t_id",
                "recipient_org_id"
            )
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
