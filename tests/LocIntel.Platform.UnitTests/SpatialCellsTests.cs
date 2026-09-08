using LocIntel.Platform.Spatial;

namespace LocIntel.Platform.UnitTests;

/// <summary>ADR 51: the spatial key is the zoom-20 tile, and tiles are contiguous key ranges.</summary>
public class SpatialCellsTests
{
    [Fact]
    public void A_point_falls_inside_the_range_of_every_tile_that_contains_it()
    {
        const double lat = 45.5122;
        const double lon = -122.6284;
        var key = SpatialCells.Key(lat, lon)!.Value;
        for (var z = 0; z <= SpatialCells.Zoom; z++)
        {
            var (x, y) = SpatialCells.Tile(lat, lon, z);
            var (lo, hi) = SpatialCells.TileRange(z, x, y);
            Assert.InRange(key, lo, hi - 1);
            // and outside its neighbour
            if (x + 1 < (1 << z))
            {
                var (nlo, nhi) = SpatialCells.TileRange(z, x + 1, y);
                Assert.False(key >= nlo && key < nhi);
            }
        }
    }

    [Fact]
    public void The_world_tile_is_one_range_over_every_key()
    {
        var (lo, hi) = SpatialCells.TileRange(0, 0, 0);
        Assert.Equal(0, lo);
        Assert.Equal(1L << (2 * SpatialCells.Zoom), hi);
        Assert.InRange(SpatialCells.Key(85, 179.99)!.Value, lo, hi - 1);
        Assert.InRange(SpatialCells.Key(-85, -180)!.Value, lo, hi - 1);
    }

    [Fact]
    public void Finer_than_a_cell_the_range_is_the_enclosing_cell()
    {
        var (lo, hi) = SpatialCells.TileRange(22, 4 * 1000, 4 * 2000);
        Assert.Equal(lo + 1, hi);
        Assert.Equal(SpatialCells.Interleave(1000, 2000), lo);
    }

    [Fact]
    public void Missing_or_absurd_coordinates_have_no_key()
    {
        Assert.Null(SpatialCells.Key(null, 1));
        Assert.Null(SpatialCells.Key(1, null));
        Assert.Null(SpatialCells.Key(double.NaN, 1));
        // beyond Mercator's edge is clamped into the edge cell, not lost
        Assert.NotNull(SpatialCells.Key(89.9, 0));
    }

    [Fact]
    public void A_cover_is_a_few_merged_ranges_that_contain_the_box()
    {
        Assert.True(BoundingBox.TryParse("-123,45,-122,46", out var box, out _));
        var ranges = SpatialCells.Cover(box);
        Assert.InRange(ranges.Count, 1, 16);
        foreach (var (lat, lon) in new[] { (45.0, -123.0), (46.0, -122.0), (45.5, -122.5) })
        {
            var key = SpatialCells.Key(lat, lon)!.Value;
            Assert.Contains(ranges, r => key >= r.lo && key < r.hi);
        }
        // sorted, non-overlapping
        for (var i = 1; i < ranges.Count; i++)
            Assert.True(ranges[i - 1].hi < ranges[i].lo);
    }

    [Fact]
    public void The_planet_covers_in_one_range_and_the_antimeridian_in_two_sides()
    {
        Assert.True(BoundingBox.TryParse("-180,-85,180,85", out var planet, out _));
        var all = SpatialCells.Cover(planet);
        Assert.Single(all);
        Assert.Equal((0L, 1L << (2 * SpatialCells.Zoom)), all[0]);

        Assert.True(BoundingBox.TryParse("170,-20,-170,-10", out var pacific, out _));
        var ranges = SpatialCells.Cover(pacific);
        Assert.Contains(
            ranges,
            r => SpatialCells.Key(-18.1, 178.4)!.Value is var k && k >= r.lo && k < r.hi
        );
        Assert.Contains(
            ranges,
            r => SpatialCells.Key(-13.8, -171.8)!.Value is var k && k >= r.lo && k < r.hi
        );
        Assert.DoesNotContain(
            ranges,
            r => SpatialCells.Key(-15, 0)!.Value is var k && k >= r.lo && k < r.hi
        );
    }
}
