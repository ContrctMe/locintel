using LocIntel.Platform.Billing;
using LocIntel.Platform.Entitlements;

namespace LocIntel.Platform.UnitTests;

/// <summary>
/// Pure invariants over the plan ladder (ADR 39): a plan naming an unknown
/// entitlement code, or a value the code's shape cannot parse, would fail
/// silently at application time - hold it here instead.
/// </summary>
public class PlanCatalogTests
{
    [Fact]
    public void Every_plan_code_exists_in_the_entitlement_catalog()
    {
        foreach (var plan in PlanCatalog.Plans)
        foreach (var code in plan.Entitlements.Keys)
            Assert.True(
                EntitlementCatalog.Definitions.ContainsKey(code),
                $"plan '{plan.Id}' grants unknown entitlement '{code}'"
            );
    }

    [Fact]
    public void Plan_values_parse_for_their_shapes()
    {
        foreach (var plan in PlanCatalog.Plans)
        foreach (var (code, value) in plan.Entitlements)
        {
            var shape = EntitlementCatalog.Definitions[code].Shape;
            var parses = shape switch
            {
                EntitlementShape.Boolean => bool.TryParse(value, out _),
                _ => long.TryParse(value, out _),
            };
            Assert.True(
                parses,
                $"plan '{plan.Id}' value '{value}' does not parse for {code} ({shape})"
            );
        }
    }

    [Fact]
    public void Paid_plans_only_raise_numeric_limits_above_the_free_defaults()
    {
        // the free tier IS the catalog defaults; a paid plan lowering a limit
        // would be a downgrade sold as an upgrade
        foreach (var plan in PlanCatalog.Plans)
        foreach (var (code, value) in plan.Entitlements)
        {
            var descriptor = EntitlementCatalog.Definitions[code];
            if (descriptor.Shape == EntitlementShape.Boolean)
                continue;
            Assert.True(
                long.Parse(value) >= long.Parse(descriptor.DefaultValue),
                $"plan '{plan.Id}' sets {code}={value}, below the free default {descriptor.DefaultValue}"
            );
        }
    }

    [Fact]
    public void Plan_ids_are_unique_and_find_works()
    {
        Assert.Equal(
            PlanCatalog.Plans.Count,
            PlanCatalog.Plans.Select(p => p.Id).Distinct().Count()
        );
        Assert.NotNull(PlanCatalog.Find("growth"));
        Assert.Null(PlanCatalog.Find("imaginary"));
    }
}
