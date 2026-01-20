using DeltaPolygon.Models;

namespace DeltaPolygon.Validators;

/// <summary>
/// Geometric validator for temporal polygons
/// Validates collinearity, self-intersections, and other geometric properties
/// </summary>
public static class PolygonValidator
{
    /// <summary>
    /// Validates that a polygon has at least 3 vertices
    /// </summary>
    public static bool HasMinimumVertices(IEnumerable<Point> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var verticesList = vertices.ToList();
        return verticesList.Count >= 3;
    }

    /// <summary>
    /// Validates that there are no consecutive collinear vertices
    /// Three points are collinear if the area of the triangle formed is zero
    /// </summary>
    public static bool HasNoCollinearVertices(IEnumerable<Point> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var verticesList = vertices.ToList();
        
        if (verticesList.Count < 3)
        {
            return true; // Less than 3 vertices cannot be collinear in a problematic way
        }

        // Close the polygon by adding the first vertex at the end if not already there
        var closedVertices = new List<Point>(verticesList);
        if (closedVertices[0] != closedVertices[closedVertices.Count - 1])
        {
            closedVertices.Add(closedVertices[0]);
        }

        // Check each consecutive trio
        for (int i = 0; i < closedVertices.Count - 2; i++)
        {
            var p1 = closedVertices[i];
            var p2 = closedVertices[i + 1];
            var p3 = closedVertices[i + 2];

            if (AreCollinear(p1, p2, p3))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if three points are collinear using the triangle area
    /// Area = 0 means the points are collinear
    /// </summary>
    private static bool AreCollinear(Point p1, Point p2, Point p3, double tolerance = 1e-10)
    {
        // Shoelace formula for triangle area
        double area = Math.Abs((p1.X * (p2.Y - p3.Y) + p2.X * (p3.Y - p1.Y) + p3.X * (p1.Y - p2.Y)) / 2.0);
        return area < tolerance;
    }

    /// <summary>
    /// Validates that the polygon does not have simple self-intersections
    /// Uses segment intersection detection algorithm
    /// NOTE: This is a simplified algorithm. For complex polygons, consider sweep line
    /// </summary>
    public static bool IsSimplePolygon(IEnumerable<Point> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var verticesList = vertices.ToList();
        
        if (verticesList.Count < 4)
        {
            return true; // Simple triangles and quadrilaterals do not self-intersect
        }

        var closedVertices = new List<Point>(verticesList);
        if (closedVertices[0] != closedVertices[closedVertices.Count - 1])
        {
            closedVertices.Add(closedVertices[0]);
        }

        int n = closedVertices.Count - 1; // Exclude the duplicated vertex

        // Check intersections between non-consecutive segments
        for (int i = 0; i < n; i++)
        {
            var seg1Start = closedVertices[i];
            var seg1End = closedVertices[(i + 1) % n];

            // Don't check with adjacent segments (i+1, i+2, i-1, i-2)
            for (int j = i + 2; j < n; j++)
            {
                // Skip the last segment that connects with the first
                if (i == 0 && j == n - 1)
                {
                    continue;
                }

                var seg2Start = closedVertices[j];
                var seg2End = closedVertices[(j + 1) % n];

                if (DoSegmentsIntersect(seg1Start, seg1End, seg2Start, seg2End))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if two line segments intersect
    /// </summary>
    private static bool DoSegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
    {
        // Use orientation to detect intersections
        double o1 = Orientation(p1, p2, p3);
        double o2 = Orientation(p1, p2, p4);
        double o3 = Orientation(p3, p4, p1);
        double o4 = Orientation(p3, p4, p2);

        // General case: segments intersect if orientations are different
        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        // Special cases: collinearity
        if (o1 == 0 && OnSegment(p1, p3, p2))
        {
            return true;
        }

        if (o2 == 0 && OnSegment(p1, p4, p2))
        {
            return true;
        }

        if (o3 == 0 && OnSegment(p3, p1, p4))
        {
            return true;
        }

        if (o4 == 0 && OnSegment(p3, p2, p4))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates the orientation of three points
    /// Returns: 0 = collinear, > 0 = clockwise, < 0 = counter-clockwise
    /// </summary>
    private static double Orientation(Point p1, Point p2, Point p3)
    {
        return (p2.Y - p1.Y) * (p3.X - p2.X) - (p2.X - p1.X) * (p3.Y - p2.Y);
    }

    /// <summary>
    /// Checks if point p2 is on segment p1-p3
    /// </summary>
    private static bool OnSegment(Point p1, Point p2, Point p3)
    {
        return p2.X <= Math.Max(p1.X, p3.X) && p2.X >= Math.Min(p1.X, p3.X) &&
               p2.Y <= Math.Max(p1.Y, p3.Y) && p2.Y >= Math.Min(p1.Y, p3.Y);
    }

    /// <summary>
    /// Determines the orientation of a polygon
    /// </summary>
    /// <param name="vertices">Polygon vertices in order</param>
    /// <returns>PolygonOrientation.Clockwise, PolygonOrientation.CounterClockwise, or PolygonOrientation.Undefined</returns>
    public static PolygonOrientation GetOrientation(IEnumerable<Point> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var verticesList = vertices.ToList();
        
        if (verticesList.Count < 3)
        {
            return PolygonOrientation.Undefined;
        }

        // Close the polygon if not closed
        var closedVertices = new List<Point>(verticesList);
        if (closedVertices[0] != closedVertices[closedVertices.Count - 1])
        {
            closedVertices.Add(closedVertices[0]);
        }

        // Calculate the sum of signed areas using the shoelace formula
        // If the result is positive, the polygon is CCW; if negative, it's CW
        double signedArea = 0;
        int n = closedVertices.Count - 1; // Exclude the duplicated vertex

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            signedArea += closedVertices[i].X * closedVertices[j].Y;
            signedArea -= closedVertices[j].X * closedVertices[i].Y;
        }

        signedArea /= 2.0;

        const double tolerance = 1e-10;
        if (Math.Abs(signedArea) < tolerance)
        {
            return PolygonOrientation.Undefined; // Zero or almost zero area
        }

        return signedArea > 0 ? PolygonOrientation.CounterClockwise : PolygonOrientation.Clockwise;
    }

    /// <summary>
    /// Validates all geometric properties of a polygon
    /// </summary>
    public static ValidationResult Validate(IEnumerable<Point> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);

        var result = new ValidationResult { IsValid = true };

        if (!HasMinimumVertices(vertices))
        {
            result.IsValid = false;
            result.Errors.Add("The polygon must have at least 3 vertices");
        }

        if (!HasNoCollinearVertices(vertices))
        {
            result.IsValid = false;
            result.Errors.Add("The polygon contains consecutive collinear vertices");
        }

        if (!IsSimplePolygon(vertices))
        {
            result.IsValid = false;
            result.Errors.Add("The polygon has self-intersections");
        }

        return result;
    }

    /// <summary>
    /// Validates all geometric properties of a polygon including orientation
    /// </summary>
    /// <param name="vertices">Polygon vertices</param>
    /// <param name="expectedOrientation">Expected orientation (optional)</param>
    public static ValidationResult ValidateWithOrientation(IEnumerable<Point> vertices, PolygonOrientation? expectedOrientation = null)
    {
        var result = Validate(vertices);
        
        if (expectedOrientation.HasValue)
        {
            var actualOrientation = GetOrientation(vertices);
            if (actualOrientation != expectedOrientation.Value && actualOrientation != PolygonOrientation.Undefined)
            {
                result.IsValid = false;
                result.Errors.Add($"The polygon has orientation {actualOrientation} but {expectedOrientation.Value} was expected");
            }
        }

        return result;
    }

    /// <summary>
    /// Orientation of a polygon
    /// </summary>
    public enum PolygonOrientation
    {
        /// <summary>
        /// Undetermined orientation (degenerate or collinear polygon)
        /// </summary>
        Undefined,
        
        /// <summary>
        /// Clockwise direction
        /// </summary>
        Clockwise,
        
        /// <summary>
        /// Counter-clockwise direction
        /// </summary>
        CounterClockwise
    }

    /// <summary>
    /// Validation result with detailed errors
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
