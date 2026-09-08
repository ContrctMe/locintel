using LocIntel.Platform.Data;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// ADR 50: one PostgreSQL image, referenced from one place. The C# constant
/// serves the AppHost and the test fixtures; the shell scripts cannot read
/// it, so <c>tools/postgres-image.sh</c> repeats the string and this test is
/// what keeps the copy honest. A stray <c>postgres:NN</c> anywhere else in
/// scripts or fixtures would boot a database without PostGIS.
/// </summary>
public class PostgresImageTests
{
    [Fact]
    public void Shell_scripts_pin_the_same_image_as_the_constant()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "postgres-image.sh"));
        Assert.Contains($"LOCINTEL_POSTGRES_IMAGE=\"{PostgresImage.Reference}\"", script);
    }

    [Fact]
    public void Reference_is_pinned_by_digest_and_is_a_postgis_image()
    {
        Assert.Contains("postgis", PostgresImage.Repository);
        Assert.Matches("^[0-9a-f]{64}$", PostgresImage.Sha256);
        Assert.EndsWith("@sha256:" + PostgresImage.Sha256, PostgresImage.Reference);
    }

    [Fact]
    public void No_script_or_fixture_names_a_plain_postgres_image()
    {
        var root = RepositoryRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(root, "tools"), "*.sh")
            .Concat(
                Directory.EnumerateFiles(
                    Path.Combine(root, "tests"),
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "LocIntel.AppHost"), "*.cs"))
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(f => File.ReadAllText(f).Contains(PlainImageNeedle))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();
        Assert.Empty(offenders);
    }

    // assembled at runtime so this file does not match itself
    private static readonly string PlainImageNeedle = "\"postgres:" + "1";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
