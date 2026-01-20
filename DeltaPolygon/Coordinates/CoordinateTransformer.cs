using DeltaPolygon.Models;

namespace DeltaPolygon.Coordinates;

/// <summary>
/// Coordinate transformer between different systems
/// Supports transformations between Cartesian and geographic coordinates (WGS84)
/// </summary>
public static class CoordinateTransformer
{
    // Earth radius in meters (WGS84)
    private const double EarthRadiusMeters = 6378137.0;

    /// <summary>
    /// Transforms a point from Cartesian to geographic coordinates (WGS84)
    /// Assumes Cartesian coordinates are in meters from an origin (0,0)
    /// </summary>
    /// <param name="point">Point in Cartesian coordinates</param>
    /// <param name="originLatitude">Origin latitude in decimal degrees</param>
    /// <param name="originLongitude">Origin longitude in decimal degrees</param>
    /// <returns>Point in geographic coordinates (longitude, latitude) in degrees</returns>
    public static Point CartesianToGeographic(Point point, double originLatitude, double originLongitude)
    {
        // Convert origin latitude and longitude to radians
        double originLatRad = DegreesToRadians(originLatitude);
        double originLonRad = DegreesToRadians(originLongitude);

        // Transformation from local Cartesian to geographic coordinates
        // Using simplified UTM projection (local approximation)
        double xMeters = point.X;
        double yMeters = point.Y;

        // Calculate latitude and longitude delta in radians
        double deltaLatRad = yMeters / EarthRadiusMeters;
        double deltaLonRad = xMeters / (EarthRadiusMeters * Math.Cos(originLatRad));

        // Convert to decimal degrees
        double latitude = originLatitude + RadiansToDegrees(deltaLatRad);
        double longitude = originLongitude + RadiansToDegrees(deltaLonRad);

        // Return as Point where X = longitude, Y = latitude
        return new Point(longitude, latitude);
    }

    /// <summary>
    /// Transforms a point from geographic coordinates (WGS84) to Cartesian
    /// Cartesian coordinates will be in meters from the specified origin
    /// </summary>
    /// <param name="point">Point in geographic coordinates (X = longitude, Y = latitude) in degrees</param>
    /// <param name="originLatitude">Origin latitude in decimal degrees</param>
    /// <param name="originLongitude">Origin longitude in decimal degrees</param>
    /// <returns>Point in Cartesian coordinates (x, y) in meters</returns>
    public static Point GeographicToCartesian(Point point, double originLatitude, double originLongitude)
    {
        // Convert origin and point latitude and longitude to radians
        double originLatRad = DegreesToRadians(originLatitude);
        double originLonRad = DegreesToRadians(originLongitude);
        
        double latitude = point.Y;
        double longitude = point.X;
        double latRad = DegreesToRadians(latitude);
        double lonRad = DegreesToRadians(longitude);

        // Calculate delta in radians
        double deltaLatRad = latRad - originLatRad;
        double deltaLonRad = lonRad - originLonRad;

        // Convert to meters using Earth radius
        double xMeters = EarthRadiusMeters * deltaLonRad * Math.Cos(originLatRad);
        double yMeters = EarthRadiusMeters * deltaLatRad;

        return new Point(xMeters, yMeters);
    }

    /// <summary>
    /// Validates that a geographic point is within valid ranges
    /// Latitude: -90 to 90 degrees
    /// Longitude: -180 to 180 degrees
    /// </summary>
    /// <param name="point">Point in geographic coordinates (X = longitude, Y = latitude)</param>
    /// <returns>true if the point is valid, false otherwise</returns>
    public static bool IsValidGeographicPoint(Point point)
    {
        double longitude = point.X;
        double latitude = point.Y;

        return latitude >= -90.0 && latitude <= 90.0 &&
               longitude >= -180.0 && longitude <= 180.0;
    }

    /// <summary>
    /// Validates that a Cartesian point is within reasonable ranges
    /// By default, assumes coordinates are in meters and validates a range of ±10,000 km
    /// </summary>
    /// <param name="point">Point in Cartesian coordinates</param>
    /// <param name="maxDistanceMeters">Maximum allowed distance from origin in meters (default 10,000,000)</param>
    /// <returns>true if the point is valid, false otherwise</returns>
    public static bool IsValidCartesianPoint(Point point, double maxDistanceMeters = 10000000)
    {
        double distance = Math.Sqrt(point.X * point.X + point.Y * point.Y);
        return distance <= maxDistanceMeters && 
               !double.IsNaN(point.X) && !double.IsNaN(point.Y) &&
               !double.IsInfinity(point.X) && !double.IsInfinity(point.Y);
    }

    /// <summary>
    /// Calculates the distance between two geographic points using the Haversine formula
    /// Returns the distance in meters
    /// </summary>
    /// <param name="point1">First geographic point (X = longitude, Y = latitude) in degrees</param>
    /// <param name="point2">Second geographic point (X = longitude, Y = latitude) in degrees</param>
    /// <returns>Distance in meters</returns>
    public static double CalculateGeographicDistance(Point point1, Point point2)
    {
        double lat1Rad = DegreesToRadians(point1.Y);
        double lon1Rad = DegreesToRadians(point1.X);
        double lat2Rad = DegreesToRadians(point2.Y);
        double lon2Rad = DegreesToRadians(point2.X);

        double deltaLat = lat2Rad - lat1Rad;
        double deltaLon = lon2Rad - lon1Rad;

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// Converts degrees to radians
    /// </summary>
    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>
    /// Converts radians to degrees
    /// </summary>
    private static double RadiansToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }
}
