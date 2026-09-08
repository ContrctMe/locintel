using Xunit.Abstractions;
using Xunit.Sdk;

namespace LocIntel.FleetTests;

/// <summary>Cases run in method-name order (the names carry a letter prefix for that reason).</summary>
public sealed class NameOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(c => c.TestMethod.Method.Name, StringComparer.Ordinal);
}
