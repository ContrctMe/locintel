namespace LocIntel.Platform.Messaging;

/// <summary>
/// Is an incoming event newer than the version a projection last applied?
/// Replicated events carry the owner row's version (its PostgreSQL xmin);
/// two of them can arrive in either order, and one can arrive twice. The
/// compare is modular in 32 bits - the way PostgreSQL itself orders
/// transaction ids - so a wrapped xmin still orders; a plain counter works
/// the same way until two versions are 2^31 apart. Equal is not newer:
/// that is the redelivery, and applying it again is wasted work at best.
/// </summary>
public static class ProjectionVersion
{
    public static bool IsNewer(long incoming, long applied) =>
        incoming != 0 && (applied == 0 || unchecked((int)((uint)incoming - (uint)applied)) > 0);
}
