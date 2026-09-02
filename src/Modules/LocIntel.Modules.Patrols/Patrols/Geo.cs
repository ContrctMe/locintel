namespace LocIntel.Modules.Patrols.Patrols;

/// <summary>Great-circle distance for checkpoint geofencing (ADR 43's haversine).</summary>
public static class Geo
{
    public const double GeofenceMeters = 100;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6_371_000;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
}
