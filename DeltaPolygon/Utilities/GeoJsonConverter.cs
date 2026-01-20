using System.Text.Json;
using System.Text.Json.Serialization;
using DeltaPolygon.Models;

namespace DeltaPolygon.Utilities;

/// <summary>
/// Utilities for converting polygons to GeoJSON format
/// </summary>
public static class GeoJsonConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Converts a temporal polygon to GeoJSON at a specific time
    /// </summary>
    /// <param name="polygon">Temporal polygon</param>
    /// <param name="time">Time to reconstruct the polygon</param>
    /// <param name="asFeature">If true, returns a Feature; if false, returns only the Geometry</param>
    /// <returns>JSON string in GeoJSON format</returns>
    public static string ToGeoJson(TemporalPolygon polygon, DateTime time, bool asFeature = true)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var points = polygon.ReconstructAt(time);
        return ToGeoJson(points, asFeature);
    }

    /// <summary>
    /// Converts a list of points to GeoJSON
    /// </summary>
    /// <param name="points">List of points that form the polygon</param>
    /// <param name="asFeature">If true, returns a Feature; if false, returns only the Geometry</param>
    /// <returns>JSON string in GeoJSON format</returns>
    public static string ToGeoJson(IEnumerable<Point> points, bool asFeature = true)
    {
        ArgumentNullException.ThrowIfNull(points);

        var pointsList = points.ToList();
        if (pointsList.Count < 3)
        {
            throw new ArgumentException("A GeoJSON polygon must have at least 3 points", nameof(points));
        }

        // GeoJSON requires the first and last point to be equal (closed polygon)
        var coordinates = new List<double[]>();
        foreach (var point in pointsList)
        {
            // GeoJSON uses [longitude, latitude] or [x, y]
            coordinates.Add(new[] { point.X, point.Y });
        }

        // Close the polygon if not closed
        if (pointsList[0] != pointsList[^1])
        {
            coordinates.Add(new[] { pointsList[0].X, pointsList[0].Y });
        }

        var geometry = new GeoJsonGeometry
        {
            Type = "Polygon",
            Coordinates = new[] { coordinates.ToArray() }
        };

        if (asFeature)
        {
            var feature = new GeoJsonFeature
            {
                Type = "Feature",
                Geometry = geometry
            };

            return JsonSerializer.Serialize(feature, JsonOptions);
        }

        return JsonSerializer.Serialize(geometry, JsonOptions);
    }

    /// <summary>
    /// Converts multiple polygons to GeoJSON FeatureCollection
    /// </summary>
    /// <param name="polygons">List of polygons with their times</param>
    /// <returns>JSON string in GeoJSON FeatureCollection format</returns>
    public static string ToGeoJsonFeatureCollection(IEnumerable<(TemporalPolygon polygon, DateTime time)> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);

        var features = polygons.Select(p => new GeoJsonFeature
        {
            Type = "Feature",
            Geometry = new GeoJsonGeometry
            {
                Type = "Polygon",
                Coordinates = new[] { CreateCoordinates(p.polygon.ReconstructAt(p.time)).ToArray() }
            }
        }).ToList();

        var featureCollection = new GeoJsonFeatureCollection
        {
            Type = "FeatureCollection",
            Features = features
        };

        return JsonSerializer.Serialize(featureCollection, JsonOptions);
    }

    private static List<double[]> CreateCoordinates(IEnumerable<Point> points)
    {
        var pointsList = points.ToList();
        var coordinates = new List<double[]>();

        foreach (var point in pointsList)
        {
            coordinates.Add(new[] { point.X, point.Y });
        }

        // Close the polygon if not closed
        if (pointsList.Count > 0 && pointsList[0] != pointsList[^1])
        {
            coordinates.Add(new[] { pointsList[0].X, pointsList[0].Y });
        }

        return coordinates;
    }

    // Helper classes for JSON serialization
    private class GeoJsonFeature
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("geometry")]
        public GeoJsonGeometry? Geometry { get; set; }

        [JsonPropertyName("properties")]
        public Dictionary<string, object>? Properties { get; set; }
    }

    private class GeoJsonGeometry
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("coordinates")]
        public double[][][]? Coordinates { get; set; }
    }

    private class GeoJsonFeatureCollection
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("features")]
        public List<GeoJsonFeature>? Features { get; set; }
    }
}
