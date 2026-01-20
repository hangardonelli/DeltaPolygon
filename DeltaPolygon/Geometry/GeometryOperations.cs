using DeltaPolygon.Models;

namespace DeltaPolygon.Geometry;

/// <summary>
/// Geometric operations for temporal polygons
/// </summary>
public static class GeometryOperations
{
    /// <summary>
    /// Calculates the area of a polygon at a specific time using the shoelace formula
    /// </summary>
    public static double GetAreaAt(TemporalPolygon polygon, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var points = polygon.ReconstructAt(time);
        return CalculateArea(points);
    }

    /// <summary>
    /// Calculates the perimeter of a polygon at a specific time
    /// </summary>
    public static double GetPerimeterAt(TemporalPolygon polygon, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var points = polygon.ReconstructAt(time);
        return CalculatePerimeter(points);
    }

    /// <summary>
    /// Calculates the centroid (center of mass) of a polygon at a specific time
    /// </summary>
    public static Point GetCentroidAt(TemporalPolygon polygon, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var points = polygon.ReconstructAt(time);
        return CalculateCentroid(points);
    }

    /// <summary>
    /// Calculates the bounding box of a polygon at a specific time
    /// </summary>
    public static BoundingBox GetBoundingBoxAt(TemporalPolygon polygon, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var points = polygon.ReconstructAt(time);
        return CalculateBoundingBox(points);
    }

    /// <summary>
    /// Checks if two polygons intersect at a specific time
    /// </summary>
    public static bool IntersectsWith(TemporalPolygon polygon1, TemporalPolygon polygon2, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon1);
        ArgumentNullException.ThrowIfNull(polygon2);

        var points1 = polygon1.ReconstructAt(time);
        var points2 = polygon2.ReconstructAt(time);

        // Simple check: if a vertex of one polygon is inside the other
        foreach (var point in points1)
        {
            if (IsPointInPolygon(point, points2))
            {
                return true;
            }
        }

        foreach (var point in points2)
        {
            if (IsPointInPolygon(point, points1))
            {
                return true;
            }
        }

        // Check for edge intersections
        return DoPolygonsIntersect(points1, points2);
    }

    /// <summary>
    /// Calculates the area using the shoelace formula
    /// </summary>
    private static double CalculateArea(List<Point> points)
    {
        if (points.Count < 3)
        {
            return 0;
        }

        double area = 0;
        int n = points.Count;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            area += points[i].X * points[j].Y;
            area -= points[j].X * points[i].Y;
        }

        return Math.Abs(area) / 2.0;
    }

    /// <summary>
    /// Calculates the perimeter by summing distances between consecutive vertices
    /// </summary>
    private static double CalculatePerimeter(List<Point> points)
    {
        if (points.Count < 3)
        {
            return 0;
        }

        double perimeter = 0;
        int n = points.Count;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            double dx = points[j].X - points[i].X;
            double dy = points[j].Y - points[i].Y;
            perimeter += Math.Sqrt(dx * dx + dy * dy);
        }

        return perimeter;
    }

    /// <summary>
    /// Calculates the centroid of the polygon
    /// </summary>
    private static Point CalculateCentroid(List<Point> points)
    {
        if (points.Count == 0)
        {
            return new Point(0, 0);
        }

        double sumX = 0;
        double sumY = 0;

        foreach (var point in points)
        {
            sumX += point.X;
            sumY += point.Y;
        }

        return new Point(sumX / points.Count, sumY / points.Count);
    }

    /// <summary>
    /// Calculates the bounding box of the polygon
    /// </summary>
    private static BoundingBox CalculateBoundingBox(List<Point> points)
    {
        if (points.Count == 0)
        {
            return new BoundingBox(new Point(0, 0), new Point(0, 0));
        }

        double minX = points[0].X;
        double maxX = points[0].X;
        double minY = points[0].Y;
        double maxY = points[0].Y;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y);
            maxY = Math.Max(maxY, point.Y);
        }

        return new BoundingBox(new Point(minX, minY), new Point(maxX, maxY));
    }

    /// <summary>
    /// Checks if a point is inside a polygon using ray casting
    /// </summary>
    private static bool IsPointInPolygon(Point point, List<Point> polygon)
    {
        if (polygon.Count < 3)
        {
            return false;
        }

        bool inside = false;
        int n = polygon.Count;

        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Checks if two polygons intersect by checking edge intersections
    /// </summary>
    private static bool DoPolygonsIntersect(List<Point> polygon1, List<Point> polygon2)
    {
        int n1 = polygon1.Count;
        int n2 = polygon2.Count;

        for (int i = 0; i < n1; i++)
        {
            var seg1Start = polygon1[i];
            var seg1End = polygon1[(i + 1) % n1];

            for (int j = 0; j < n2; j++)
            {
                var seg2Start = polygon2[j];
                var seg2End = polygon2[(j + 1) % n2];

                if (DoSegmentsIntersect(seg1Start, seg1End, seg2Start, seg2End))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if two line segments intersect
    /// </summary>
    private static bool DoSegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
    {
        double o1 = Orientation(p1, p2, p3);
        double o2 = Orientation(p1, p2, p4);
        double o3 = Orientation(p3, p4, p1);
        double o4 = Orientation(p3, p4, p2);

        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates the orientation of three points
    /// </summary>
    private static double Orientation(Point p1, Point p2, Point p3)
    {
        return (p2.Y - p1.Y) * (p3.X - p2.X) - (p2.X - p1.X) * (p3.Y - p2.Y);
    }

    /// <summary>
    /// Represents a bounding box
    /// </summary>
    public class BoundingBox
    {
        public Point Min { get; }
        public Point Max { get; }

        public BoundingBox(Point min, Point max)
        {
            Min = min;
            Max = max;
        }

        public double Width => Max.X - Min.X;
        public double Height => Max.Y - Min.Y;
    }
}
