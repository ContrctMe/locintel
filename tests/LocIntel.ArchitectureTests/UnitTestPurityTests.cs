using System.Xml.Linq;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// Unit tests are pure logic - no mocks, no fakes, no persistence substitutes
/// (ADR 38). Enforced against the PROJECT FILE, not the compiled assembly: a
/// forbidden reference nothing has used yet has still broken the rule, because
/// it is the reference that makes the wrong test cheap to write next.
/// Discovery is by the *.UnitTests.csproj naming pattern, so adding a
/// unit-test project needs no registration here.
/// </summary>
public class UnitTestPurityTests
{
    private static readonly string[] ForbiddenPackages =
    [
        "Moq",
        "NSubstitute",
        "FakeItEasy",
        "Microsoft.EntityFrameworkCore.InMemory",
        "Microsoft.Data.Sqlite",
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Testcontainers",
        "Respawn",
        "Npgsql",
        "Microsoft.AspNetCore.Mvc.Testing",
    ];

    private static readonly string[] ForbiddenProjectReferences =
    [
        "LocIntel.Api",
        "LocIntel.Integrations",
    ];

    public static TheoryData<string> UnitTestProjects()
    {
        var data = new TheoryData<string>();
        foreach (var project in FindUnitTestProjects())
            data.Add(project);
        return data;
    }

    [Fact]
    public void At_least_one_unit_test_project_exists()
    {
        // the pattern must stay alive in the template: forks inherit the tier
        Assert.NotEmpty(FindUnitTestProjects());
    }

    [Theory]
    [MemberData(nameof(UnitTestProjects))]
    public void Unit_test_projects_declare_no_infrastructure(string projectPath)
    {
        var project = XDocument.Load(projectPath);

        var packages = project
            .Descendants("PackageReference")
            .Select(p => p.Attribute("Include")?.Value ?? "")
            .ToList();
        foreach (var package in packages)
            Assert.False(
                ForbiddenPackages.Any(f =>
                    package.Equals(f, StringComparison.OrdinalIgnoreCase)
                    || package.StartsWith(f + ".", StringComparison.OrdinalIgnoreCase)
                ),
                $"{Path.GetFileName(projectPath)} declares '{package}' - unit tests are pure logic; "
                    + "behavior needing infrastructure belongs in LocIntel.IntegrationTests"
            );

        var references = project
            .Descendants("ProjectReference")
            .Select(p => Path.GetFileNameWithoutExtension(p.Attribute("Include")?.Value ?? ""))
            .ToList();
        foreach (var reference in references)
            Assert.False(
                ForbiddenProjectReferences.Any(f =>
                    reference.StartsWith(f, StringComparison.OrdinalIgnoreCase)
                ),
                $"{Path.GetFileName(projectPath)} references '{reference}' - unit tests must not "
                    + "reach the host or integration adapters"
            );
    }

    private static List<string> FindUnitTestProjects()
    {
        var testsRoot = RepoDir("tests");
        return Directory
            .EnumerateFiles(testsRoot, "*.UnitTests.csproj", SearchOption.AllDirectories)
            .OrderBy(p => p)
            .ToList();
    }

    private static string RepoDir(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, name)))
            dir = dir.Parent;
        return Path.Combine(
            (dir ?? throw new InvalidOperationException("repo root not found")).FullName,
            name
        );
    }
}
