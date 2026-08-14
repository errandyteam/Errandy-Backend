namespace Errandy.Application.Common.Utils;

public static class LocationPrivacyHelper
{
    public static (double Latitude, double Longitude) Fuzz(double latitude, double longitude)
    {
        return (Math.Round(latitude, 2), Math.Round(longitude, 2));
    }
}