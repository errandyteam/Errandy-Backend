namespace Errandy.Application.Common.Utils;

/// <summary>
/// Straight-line distance between two lat/lng points using the Haversine
/// formula. Good enough for the MVP's 5km "nearby errands" filter (PRD
/// section "Runner Discovery"). Note: this runs in-memory after pulling
/// candidate rows from the DB — fine at MVP scale, but if the Created-errand
/// table grows large, swap to a real spatial query (PostGIS ST_DWithin or
/// SQL Server geography type) instead of computing distance for every row.
/// </summary>
public static class GeoDistanceCalculator
{
    private const double EarthRadiusKm = 6371.0;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
