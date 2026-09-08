using System.Reflection;
using Wolverine.Attributes;

namespace LocIntel.ArchitectureTests;

/// <summary>
/// [Transactional(typeof(X))] names the transaction owner for a chain that
/// touches more than one DbContext. Wolverine resolves that at CODEGEN time,
/// so naming a context the method never actually takes fails at HOST STARTUP -
/// which surfaces as every test in every class failing at 1ms with no usable
/// message. A fork lost time to exactly that.
///
/// This turns it into a named build failure. The check is deliberately
/// simple: the declared context must be a parameter of the method that
/// declares it, which is how every transaction owner in this codebase is
/// supplied.
/// </summary>
public class TransactionalAttributeTests
{
    [Fact]
    public void Explicit_transactions_on_GET_endpoints_are_lightweight()
    {
        var offenders = ProductAssemblies()
            .SelectMany(a => a.GetTypes())
            .SelectMany(t =>
                t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            )
            .Where(m => m.IsDefined(typeof(Wolverine.Http.WolverineGetAttribute)))
            .Where(m =>
                m.GetCustomAttribute<TransactionalAttribute>()
                    is { Mode: not Wolverine.Persistence.TransactionMiddlewareMode.Lightweight }
            )
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToArray();
        Assert.True(
            offenders.Length == 0,
            "GET handlers must not hold an eager transaction while calling other modules: "
                + string.Join(", ", offenders)
        );
    }

    [Fact]
    public void Declared_transaction_owners_are_actually_injected()
    {
        var offenders = new List<string>();

        foreach (var assembly in ProductAssemblies())
        foreach (var type in assembly.GetTypes())
        foreach (
            var method in type.GetMethods(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            )
        )
        {
            var attribute = method.GetCustomAttribute<TransactionalAttribute>();
            // only the typed form names an owner; the bare form infers it
            var declared = attribute?.GetType().GetProperty("DbContextType")?.GetValue(attribute);
            if (declared is not Type owner)
                continue;

            if (!Reaches(method.GetParameters().Select(p => p.ParameterType), owner, depth: 3))
                offenders.Add(
                    $"{type.Name}.{method.Name} declares {owner.Name}, "
                        + "but nothing in its dependency chain supplies it"
                );
        }

        Assert.True(
            offenders.Count == 0,
            "[Transactional(typeof(X))] naming a context the method does not inject - "
                + "this fails at host startup, not here, and takes the whole suite with it:\n  "
                + string.Join("\n  ", offenders)
        );
    }

    /// <summary>
    /// Wolverine resolves the owner through the whole chain, not just the
    /// method signature - EntitlementsService holding an EntitlementsDbContext
    /// counts. So walk constructor dependencies too, bounded to keep the scan
    /// cheap and terminating.
    /// </summary>
    private static bool Reaches(IEnumerable<Type> candidates, Type owner, int depth)
    {
        foreach (var candidate in candidates)
        {
            if (candidate == owner)
                return true;
            if (
                depth <= 0
                || candidate.IsPrimitive
                || candidate.Namespace?.StartsWith("System") == true
            )
                continue;

            var dependencies = candidate
                .GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType);
            if (Reaches(dependencies, owner, depth - 1))
                return true;
        }
        return false;
    }

    private static IEnumerable<Assembly> ProductAssemblies() =>
        LocIntel
            .Api.ModuleCatalog.All.Select(m => m.DbContextType.Assembly)
            .Append(typeof(LocIntel.Api.ModuleCatalog).Assembly)
            .Distinct();
}
