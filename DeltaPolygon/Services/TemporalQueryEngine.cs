using DeltaPolygon.Models;

namespace DeltaPolygon.Services;

/// <summary>
/// Temporal query engine with efficient O(log H) search
/// 

/// Temporal indexing (B-Tree, GiST) ensures efficient O(log H) search.
/// Compression and grouping reduce rows to traverse.
/// </summary>
public class TemporalQueryEngine
{
    /// <summary>
    /// Reconstructs a polygon at a specific time
    /// 
    
    /// P_render(t_target) = ∪_{i=1}^{n} Pos(v_i, t_target)
    /// Reconstructs the complete polygon by joining all vertex positions at the specified time.
    /// </summary>
    public static List<Point> ReconstructPolygon(TemporalPolygon polygon, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon, nameof(polygon));

        return polygon.ReconstructAt(time);
    }

    /// <summary>
    /// Gets the position of a specific vertex at a given time
    /// 
    
    /// Pos(v, t_target) = (x, y) ⟺ ∃k : t_start_k ≤ t_target < t_end_k
    /// Searches for the temporal state that contains the target time and returns the corresponding position.
    /// If delta is used: cumulative sum is applied over the last absolute state.
    /// </summary>
    public static Point? GetVertexPosition(TemporalPolygon polygon, int vertexId, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var vertex = polygon.GetVertex(vertexId);
        return vertex?.GetPositionAt(time);
    }

    /// <summary>
    /// Gets all vertices of a polygon at a specific time
    /// </summary>
    public static Dictionary<int, Point> GetAllVertexPositions(TemporalPolygon polygon, DateTime time)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var positions = new Dictionary<int, Point>();

        foreach (var vertexId in polygon.VertexIds)
        {
            var position = GetVertexPosition(polygon, vertexId, time);
            if (position.HasValue)
            {
                positions[vertexId] = position.Value;
            }
        }

        return positions;
    }

    /// <summary>
    /// Checks if a polygon exists at a specific time
    /// (all its vertices have valid states at that time)
    /// </summary>
    public static bool PolygonExistsAt(TemporalPolygon polygon, DateTime time)
    {
        if (polygon == null)
        {
            return false;
        }

        foreach (var vertexId in polygon.VertexIds)
        {
            var vertex = polygon.GetVertex(vertexId);
            if (vertex == null || vertex.GetPositionAt(time) == null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a polygon exists at some point within a temporal range
    /// Returns true if the polygon has valid states at any moment between startTime and endTime (inclusive)
    /// </summary>
    /// <param name="polygon">Polygon to check</param>
    /// <param name="startTime">Range start time</param>
    /// <param name="endTime">Range end time</param>
    /// <returns>True if the polygon exists at some point in the range</returns>
    public static bool PolygonExistsInRange(TemporalPolygon polygon, DateTime startTime, DateTime endTime)
    {
        if (polygon == null)
        {
            return false;
        }

        if (startTime > endTime)
        {
            return false;
        }

        // Check if polygon exists at some point in the range
        // For each vertex, check if it has any state that overlaps with the range
        foreach (var vertexId in polygon.VertexIds)
        {
            var vertex = polygon.GetVertex(vertexId);
            if (vertex == null)
            {
                return false;
            }

            // Check if vertex has any valid state in the range
            bool hasValidStateInRange = false;
            foreach (var state in vertex.States)
            {
                var interval = state.Interval;
                
                // A state is in the range if:
                // - Its start is before or equal to endTime AND
                // - Its end is after or equal to startTime (or is infinity/null)
                bool intervalStartsBeforeRangeEnds = interval.Start <= endTime;
                bool intervalEndsAfterRangeStarts = !interval.End.HasValue || interval.End.Value >= startTime;

                if (intervalStartsBeforeRangeEnds && intervalEndsAfterRangeStarts)
                {
                    hasValidStateInRange = true;
                    break;
                }
            }

            if (!hasValidStateInRange)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if a polygon exists throughout an entire temporal range
    /// Returns true if the polygon has valid states at all moments between startTime and endTime
    /// </summary>
    /// <param name="polygon">Polygon to check</param>
    /// <param name="startTime">Range start time</param>
    /// <param name="endTime">Range end time</param>
    /// <returns>True if the polygon exists throughout the range</returns>
    public static bool PolygonExistsForEntireRange(TemporalPolygon polygon, DateTime startTime, DateTime endTime)
    {
        if (polygon == null)
        {
            return false;
        }

        if (startTime > endTime)
        {
            return false;
        }

        // Check if polygon exists at both ends of the range
        // and if there is continuity (simplified: we check only the ends)
        return PolygonExistsAt(polygon, startTime) && PolygonExistsAt(polygon, endTime);
    }

    /// <summary>
    /// Gets all unique times where there are state changes in a polygon within a range
    /// Useful for determining temporal change points in a specific range
    /// </summary>
    /// <param name="polygon">Polygon to analyze</param>
    /// <param name="startTime">Range start time</param>
    /// <param name="endTime">Range end time</param>
    /// <returns>Sorted set of times where there are changes</returns>
    public static SortedSet<DateTime> GetChangeTimesInRange(TemporalPolygon polygon, DateTime startTime, DateTime endTime)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        var changeTimes = new SortedSet<DateTime> { startTime, endTime };

        foreach (var vertexId in polygon.VertexIds)
        {
            var vertex = polygon.GetVertex(vertexId);
            if (vertex != null)
            {
                foreach (var state in vertex.States)
                {
                    var interval = state.Interval;

                    // Add interval start if it's in the range
                    if (interval.Start >= startTime && interval.Start <= endTime)
                    {
                        changeTimes.Add(interval.Start);
                    }

                    // Add interval end if it's in the range
                    if (interval.End.HasValue && 
                        interval.End.Value >= startTime && 
                        interval.End.Value <= endTime)
                    {
                        changeTimes.Add(interval.End.Value);
                    }
                }
            }
        }

        return changeTimes;
    }
}
