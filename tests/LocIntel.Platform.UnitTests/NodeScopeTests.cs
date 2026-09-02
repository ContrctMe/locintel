using LocIntel.Platform.Kernel;

namespace LocIntel.Platform.UnitTests;

/// <summary>
/// Pure logic (ADR 38): Covers() is the write-side scope gate - a wrong
/// prefix match here IS a cross-subtree write. Unit tests because the
/// semantics are string-level and deterministic; the DB-side ltree
/// translation is proven by the integration suite.
/// </summary>
public class NodeScopeTests
{
    private static readonly OrgId Org = OrgId.New();

    [Fact]
    public void Nothing_covers_no_path()
    {
        Assert.False(NodeScope.Nothing.Covers("a"));
        Assert.False(NodeScope.Nothing.Covers(""));
    }

    [Fact]
    public void EntireOrg_covers_everything()
    {
        var scope = new NodeScope.EntireOrg(Org);
        Assert.True(scope.Covers("a"));
        Assert.True(scope.Covers("a.b.c"));
    }

    [Theory]
    [InlineData("a.b", true)] // the subtree root itself
    [InlineData("a.b.c", true)] // a descendant
    [InlineData("a.b.c.d", true)] // any depth
    [InlineData("a", false)] // an ancestor is NOT covered
    [InlineData("a.bc", false)] // label-boundary: "a.b" must not match "a.bc"
    [InlineData("x.b", false)] // sibling subtree
    public void Subtree_coverage_respects_label_boundaries(string path, bool covered)
    {
        var scope = new NodeScope.Subtrees(Org, ["a.b"]);
        Assert.Equal(covered, scope.Covers(path));
    }

    [Fact]
    public void Multiple_subtrees_union()
    {
        var scope = new NodeScope.Subtrees(Org, ["a.b", "a.c"]);
        Assert.True(scope.Covers("a.b.x"));
        Assert.True(scope.Covers("a.c"));
        Assert.False(scope.Covers("a.d"));
    }
}
