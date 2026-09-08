using System.Linq.Expressions;
using Npgsql;

namespace LocIntel.Platform.Data;

/// <summary>
/// Subtree predicates on the TEXT form of an ltree path (ADR 51). Under row
/// security Postgres refuses ltree's <c>&lt;@</c> as an index condition (its
/// operator functions are not leakproof), so a scoped query over a large
/// table is a sequential scan for the app role. Text comparison is leakproof,
/// and in the "C" collation a subtree is one contiguous range:
/// <c>path = p OR (path &gt;= p || '.' AND path &lt; p || '/')</c> - '/' is
/// the character after '.', so nothing else can fall inside.
/// </summary>
public static class PathKeys
{
    /// <summary>The bounds of every descendant of <paramref name="path"/> (not the path itself).</summary>
    public static (string lo, string hi) DescendantRange(string path) => (path + ".", path + "/");

    /// <summary>
    /// <c>e =&gt; e.PathText is under ANY of paths</c> as an expression tree,
    /// each bound a query parameter. <typeparamref name="T"/> must expose a
    /// string <c>PathText</c> property (see <see cref="IPathIndexed"/>).
    /// </summary>
    public static Expression<Func<T, bool>> UnderAny<T>(IReadOnlyList<string> paths)
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        var property =
            typeof(T).GetProperty(nameof(IPathIndexed.PathText))
            ?? throw new InvalidOperationException(
                $"{typeof(T).Name} has no PathText; implement IPathIndexed"
            );
        var pathText = Expression.Property(parameter, property);
        var compareTo = typeof(string).GetMethod(nameof(string.CompareTo), [typeof(string)])!;
        Expression? any = null;
        foreach (var path in paths)
        {
            var (lo, hi) = DescendantRange(path);
            var self = Expression.Equal(pathText, Captured(path));
            var below = Expression.AndAlso(
                Expression.GreaterThanOrEqual(
                    Expression.Call(pathText, compareTo, Captured(lo)),
                    Expression.Constant(0)
                ),
                Expression.LessThan(
                    Expression.Call(pathText, compareTo, Captured(hi)),
                    Expression.Constant(0)
                )
            );
            var term = Expression.OrElse(self, below);
            any = any is null ? term : Expression.OrElse(any, term);
        }
        return Expression.Lambda<Func<T, bool>>(any ?? Expression.Constant(false), parameter);
    }

    /// <summary>
    /// The same predicate as SQL text for a raw query: <paramref name="column"/>
    /// is the path-text column, parameters are appended to
    /// <paramref name="parameters"/> named <c>{prefix}{i}</c>, <c>{prefix}{i}lo</c>,
    /// <c>{prefix}{i}hi</c>. Returns a parenthesised expression.
    /// </summary>
    public static string Sql(
        string column,
        IReadOnlyList<string> paths,
        string prefix,
        ICollection<NpgsqlParameter> parameters
    )
    {
        if (paths.Count == 0)
            return "false";
        var terms = new List<string>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var (lo, hi) = DescendantRange(paths[i]);
            parameters.Add(new NpgsqlParameter($"{prefix}{i}", paths[i]));
            parameters.Add(new NpgsqlParameter($"{prefix}{i}lo", lo));
            parameters.Add(new NpgsqlParameter($"{prefix}{i}hi", hi));
            terms.Add(
                $"{column} = @{prefix}{i} OR ({column} >= @{prefix}{i}lo AND {column} < @{prefix}{i}hi)"
            );
        }
        return "(" + string.Join(" OR ", terms) + ")";
    }

    // member access on a captured object is what EF's parameter extraction
    // treats as a closure, so each bound becomes a query parameter rather
    // than an inlined literal (one plan for every subtree)
    private static MemberExpression Captured(string value) =>
        Expression.Property(Expression.Constant(new Box(value)), nameof(Box.Value));

    private sealed record Box(string Value);
}
