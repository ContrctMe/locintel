using LocIntel.Platform.Data;
using LocIntel.Platform.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LocIntel.Platform.UnitTests;

/// <summary>ADR 51: a subtree is one text range under the "C" collation.</summary>
public class PathKeysTests
{
    private sealed record Row(string PathText) : IPathIndexed
    {
        public OrgId OrgId => new(Guid.Empty);
        public LTree Path => new(PathText);
    }

    private static bool Under(string path, params string[] scope) =>
        PathKeys.UnderAny<Row>(scope).Compile()(new Row(path));

    [Fact]
    public void A_node_its_descendants_and_nothing_else_are_under_a_path()
    {
        Assert.True(Under("a.b", "a.b"));
        Assert.True(Under("a.b.c", "a.b"));
        Assert.True(Under("a.b.c.d", "a.b"));
        Assert.False(Under("a.b2", "a.b")); // a sibling whose label extends the prefix
        Assert.False(Under("a.b-1", "a.b")); // '-' sorts before '.', still not below
        Assert.False(Under("a", "a.b"));
        Assert.False(Under("a.c", "a.b"));
    }

    [Fact]
    public void Any_of_several_subtrees_counts_and_an_empty_scope_matches_nothing()
    {
        Assert.True(Under("x.y", "a", "x"));
        Assert.False(Under("z", "a", "x"));
        Assert.False(Under("a"));
    }

    [Fact]
    public void The_sql_form_is_the_same_range_with_parameters()
    {
        var parameters = new List<NpgsqlParameter>();
        var sql = PathKeys.Sql("f.path_text", ["a.b", "c"], "scope", parameters);
        Assert.Equal(
            "(f.path_text = @scope0 OR (f.path_text >= @scope0lo AND f.path_text < @scope0hi) OR f.path_text = @scope1 OR (f.path_text >= @scope1lo AND f.path_text < @scope1hi))",
            sql
        );
        Assert.Equal(
            ["a.b", "a.b.", "a.b/", "c", "c.", "c/"],
            parameters.Select(p => (string)p.Value!)
        );
        Assert.Equal("false", PathKeys.Sql("p", [], "s", parameters));
    }
}
