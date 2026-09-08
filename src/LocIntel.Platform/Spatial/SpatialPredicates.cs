using System.Linq.Expressions;

namespace LocIntel.Platform.Spatial;

/// <summary>A point row that carries its spatial key beside its coordinates (ADR 51).</summary>
public interface ISpatialPoint
{
    long? Cell { get; }
    double? Latitude { get; }
    double? Longitude { get; }
}

/// <summary>
/// Builds the "inside this viewport" predicate a bounding box turns into
/// (ADR 49, ADR 51): the covering cell ranges narrow through the btree, the
/// coordinates make it exact. Every term is a leakproof comparison, so the
/// plan is an index range for the app role under row security. Assembled as
/// an expression tree because EF cannot translate a client-side list of
/// ranges; each bound is a query parameter, so the plan is cached.
/// </summary>
public static class SpatialPredicates
{
    public static Expression<Func<T, bool>> InViewport<T>(BoundingBox box)
        where T : ISpatialPoint
    {
        var parameter = Expression.Parameter(typeof(T), "s");
        var cell = Expression.Property(parameter, nameof(ISpatialPoint.Cell));
        var latitude = Expression.Property(parameter, nameof(ISpatialPoint.Latitude));
        var longitude = Expression.Property(parameter, nameof(ISpatialPoint.Longitude));

        Expression? ranges = null;
        foreach (var (lo, hi) in SpatialCells.Cover(box))
        {
            var term = Expression.AndAlso(
                Expression.GreaterThanOrEqual(cell, Captured<long?>(lo)),
                Expression.LessThan(cell, Captured<long?>(hi))
            );
            ranges = ranges is null ? term : Expression.OrElse(ranges, term);
        }

        var inLatitude = Expression.AndAlso(
            Expression.GreaterThanOrEqual(latitude, Captured<double?>(box.South)),
            Expression.LessThanOrEqual(latitude, Captured<double?>(box.North))
        );
        var west = Expression.GreaterThanOrEqual(longitude, Captured<double?>(box.West));
        var east = Expression.LessThanOrEqual(longitude, Captured<double?>(box.East));
        // across the antimeridian a box is "west of the line OR east of it"
        var inLongitude = box.CrossesAntimeridian
            ? Expression.OrElse(west, east)
            : Expression.AndAlso(west, east);

        var notNull = Expression.NotEqual(cell, Expression.Constant(null, typeof(long?)));
        var body = Expression.AndAlso(
            Expression.AndAlso(notNull, ranges!),
            Expression.AndAlso(inLatitude, inLongitude)
        );
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    // member access on a captured object is what EF's parameter extraction
    // treats as a closure, so each bound becomes a query parameter
    private static MemberExpression Captured<TValue>(TValue value) =>
        Expression.Property(Expression.Constant(new Box<TValue>(value)), nameof(Box<TValue>.Value));

    private sealed record Box<TValue>(TValue Value);
}
