using Xunit.Abstractions;
using Xunit.Sdk;

namespace LocIntel.IntegrationTests;

/// <summary>
/// Shuffles the tests within each class when LOCINTEL_TEST_SHUFFLE is set to a
/// seed (any integer). Order-dependence is invisible until something perturbs
/// the order - a fork renaming the product changed xUnit's name-hash ordering
/// and two latent dependencies in THIS suite surfaced at once. Running
/// shuffled in CI turns that from a fork's surprise into our build failure.
///
/// Off by default so local runs stay reproducible; the seed is printed by CI
/// so a failure can be replayed exactly.
/// </summary>
public sealed class RandomOrderer : ITestCaseOrderer
{
    public const string EnvironmentVariable = "LOCINTEL_TEST_SHUFFLE";

    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        var seed = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!int.TryParse(seed, out var value))
            return testCases;

        var random = new Random(value);
        return testCases.OrderBy(_ => random.Next());
    }
}
