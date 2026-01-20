namespace DeltaPolygon.Coordinates;

/// <summary>
/// Enum representing different coordinate systems
/// </summary>
public enum CoordinateSystem
{
    /// <summary>
    /// Cartesian coordinate system (x, y)
    /// Standard system for planar geometry
    /// </summary>
    Cartesian,

    /// <summary>
    /// Geographic coordinate system (longitude, latitude) using WGS84
    /// Standard for geographic information systems (GIS)
    /// </summary>
    Geographic
}
