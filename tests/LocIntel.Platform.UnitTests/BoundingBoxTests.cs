using LocIntel.Platform.Spatial;
using NetTopologySuite.Geometries;

namespace LocIntel.Platform.UnitTests;

/// <summary>ADR 49: the viewport box is a pure value - parsing, the antimeridian split, the 90° cut.</summary>
public class BoundingBoxTests
{
    [Fact]
    public void Parses_west_south_east_north()
    {
        Assert.True(BoundingBox.TryParse("-122.8, 45.4, -122.5, 45.6", out var box, out var error));
        Assert.Null(error);
        Assert.Equal(new BoundingBox(-122.8, 45.4, -122.5, 45.6), box);
        Assert.False(box.CrossesAntimeridian);
        var envelope = Assert.Single(box.Envelopes());
        Assert.Equal(4326, envelope.SRID);
        Assert.Equal(new Envelope(-122.8, -122.5, 45.4, 45.6), envelope.EnvelopeInternal);
    }

    [Fact]
    public void A_box_with_west_past_east_splits_at_the_antimeridian()
    {
        Assert.True(BoundingBox.TryParse("170,-20,-170,-10", out var box, out _));
        Assert.True(box.CrossesAntimeridian);
        var envelopes = box.Envelopes();
        Assert.Equal(2, envelopes.Count);
        Assert.Equal(new Envelope(170, 180, -20, -10), envelopes[0].EnvelopeInternal);
        Assert.Equal(new Envelope(-180, -170, -20, -10), envelopes[1].EnvelopeInternal);
    }

    [Fact]
    public void Spans_wider_than_ninety_degrees_are_cut_so_no_edge_is_antipodal()
    {
        Assert.True(BoundingBox.TryParse("-180,-90,180,90", out var planet, out _));
        var envelopes = planet.Envelopes();
        Assert.Equal(8, envelopes.Count); // 4 longitude bands x 2 latitude bands
        Assert.All(
            envelopes,
            e =>
            {
                Assert.True(e.EnvelopeInternal.Width <= BoundingBox.MaxSpanDegrees);
                Assert.True(e.EnvelopeInternal.Height <= BoundingBox.MaxSpanDegrees);
            }
        );
        // the pieces tile the box exactly
        var union = envelopes
            .Select(e => e.EnvelopeInternal)
            .Aggregate(
                new Envelope(),
                (a, e) =>
                {
                    a.ExpandToInclude(e);
                    return a;
                }
            );
        Assert.Equal(new Envelope(-180, 180, -90, 90), union);
        Assert.Equal(360 * 180, envelopes.Sum(e => e.EnvelopeInternal.Area), 6);
    }

    [Fact]
    public void A_wide_box_across_the_antimeridian_cuts_both_sides()
    {
        Assert.True(BoundingBox.TryParse("60,0,-60,10", out var box, out _)); // 240° of longitude
        var envelopes = box.Envelopes();
        Assert.Equal(4, envelopes.Count); // 60..180 in two, -180..-60 in two
        Assert.All(envelopes, e => Assert.True(e.EnvelopeInternal.Width <= 90));
    }

    [Theory]
    [InlineData("", "bbox is empty")]
    [InlineData("1,2,3", "bbox must be west,south,east,north")]
    [InlineData("a,2,3,4", "bbox must be four finite numbers")]
    [InlineData("NaN,2,3,4", "bbox must be four finite numbers")]
    [InlineData("-181,0,10,10", "bbox longitude out of range")]
    [InlineData("0,-91,10,10", "bbox latitude out of range")]
    [InlineData("0,20,10,10", "bbox south must not exceed north")]
    public void Rejects_malformed_boxes_with_a_message(string text, string expected)
    {
        Assert.False(BoundingBox.TryParse(text, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Parsing_is_culture_invariant()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.True(BoundingBox.TryParse("-122.8,45.4,-122.5,45.6", out var box, out _));
            Assert.Equal(-122.8, box.West);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    private sealed record Point(string Name, double? Latitude, double? Longitude) : ISpatialPoint
    {
        public long? Cell => SpatialCells.Key(Latitude, Longitude);
    }

    [Fact]
    public void InViewport_is_cell_ranges_made_exact_by_the_coordinates()
    {
        Assert.True(BoundingBox.TryParse("170,-20,-170,-10", out var box, out _));
        var predicate = SpatialPredicates.InViewport<Point>(box).Compile();
        Assert.True(predicate(new Point("Suva", -18.1, 178.4)));
        Assert.True(predicate(new Point("Apia", -13.8, -171.8)));
        Assert.False(predicate(new Point("Hawthorne", 45.5, -122.6)));
        Assert.False(predicate(new Point("Nowhere", null, null)));

        Assert.True(BoundingBox.TryParse("-123,45,-122,46", out var portland, out _));
        var inPortland = SpatialPredicates.InViewport<Point>(portland).Compile();
        Assert.True(inPortland(new Point("Hawthorne", 45.5122, -122.6284)));
        // inside the covering cells but outside the box: the coordinates decide
        Assert.False(inPortland(new Point("Just north", 46.01, -122.6)));
    }
}
