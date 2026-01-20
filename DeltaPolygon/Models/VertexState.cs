using DeltaPolygon.Utilities;

namespace DeltaPolygon.Models;

/// <summary>
/// Represents a temporal state of a vertex: (Δx, Δy, [t_start, t_end))
/// Supports relative deltas, absolute states, and temporal functions
/// Can also represent a group of vertices with identical changes
/// 

/// Allows storing a single state for multiple vertices with identical changes,
/// reducing storage space.
/// 

/// Allows storing a temporal function instead of individual states,
/// enabling predictable movement and efficient storage.
/// </summary>
public class VertexState
{
    public Point Delta { get; }
    public TimeInterval Interval { get; }
    public bool IsAbsolute { get; }
    public Point? AbsolutePosition { get; } // Only used if IsAbsolute is true

    /// <summary>
    /// List of additional vertex IDs sharing this state
    /// If null or empty, this state only applies to the containing vertex
    /// If it has elements, this state applies to all listed vertices
    /// 
    
    /// Example: If VertexIds = [10, 11, 12], then vertices 10, 11, and 12
    /// have exactly the same change (delta or absolute position) over the same interval.
    /// </summary>
    public IReadOnlyList<int>? GroupedVertexIds { get; }

    /// <summary>
    /// Optional temporal function to compute position based on time
    /// If present, it is used instead of Delta/AbsolutePosition to get positions
    /// 
    
    /// Allows storing functions like linear or circular motion instead of
    /// discrete states, reducing storage space and enabling
    /// precise interpolation at any moment in the interval.
    /// </summary>
    public TemporalFunction? TemporalFunction { get; }

    /// <summary>
    /// Creates a state with a relative delta
    /// </summary>
    /// <param name="delta">Relative delta</param>
    /// <param name="interval">Time interval</param>
    /// <param name="groupedVertexIds">Additional vertex IDs sharing this state (optional)</param>
    public VertexState(Point delta, TimeInterval interval, IReadOnlyList<int>? groupedVertexIds = null)
    {
        Delta = delta;
        Interval = interval;
        IsAbsolute = false;
        AbsolutePosition = null;
        GroupedVertexIds = groupedVertexIds;
        TemporalFunction = null;
    }

    /// <summary>
    /// Creates a state with an absolute position
    /// </summary>
    /// <param name="absolutePosition">Absolute position</param>
    /// <param name="interval">Time interval</param>
    /// <param name="isAbsolute">Must be true for absolute position</param>
    /// <param name="groupedVertexIds">Additional vertex IDs sharing this state (optional)</param>
    public VertexState(
        Point absolutePosition,
        TimeInterval interval,
        bool isAbsolute,
        IReadOnlyList<int>? groupedVertexIds = null)
    {
        if (!isAbsolute)
        {
            throw new ArgumentException("For absolute position, isAbsolute must be true", nameof(isAbsolute));
        }

        AbsolutePosition = absolutePosition;
        Delta = new Point(0, 0);
        Interval = interval;
        IsAbsolute = true;
        GroupedVertexIds = groupedVertexIds;
        TemporalFunction = null;
    }

    /// <summary>
    /// Creates a state with a temporal function
    /// The temporal function is used to calculate position over time
    /// within the specified interval
    /// </summary>
    /// <param name="temporalFunction">Temporal function to compute positions</param>
    /// <param name="interval">Time interval where the function is valid</param>
    /// <param name="groupedVertexIds">Additional vertex IDs sharing this function (optional)</param>
    public VertexState(
        TemporalFunction temporalFunction,
        TimeInterval interval,
        IReadOnlyList<int>? groupedVertexIds = null)
    {
        if (temporalFunction == null)
        {
            throw new ArgumentNullException(nameof(temporalFunction));
        }

        TemporalFunction = temporalFunction;
        Interval = interval;

        // For compatibility with normal states, set default values
        // The function will be used to obtain actual positions
        AbsolutePosition = null;
        Delta = new Point(0, 0);
        IsAbsolute = false;
        GroupedVertexIds = groupedVertexIds;
    }

    /// <summary>
    /// Determines if this state represents a group of vertices
    /// </summary>
    public bool IsGrouped => GroupedVertexIds != null && GroupedVertexIds.Count > 0;

    /// <summary>
    /// Determines if this state is equivalent to another (same delta/position and interval)
    /// Does not consider grouped IDs for comparison
    /// </summary>
    public bool IsEquivalentTo(VertexState other)
    {
        if (other == null)
        {
            return false;
        }

        if (IsAbsolute != other.IsAbsolute)
        {
            return false;
        }

        if (IsAbsolute)
        {
            return AbsolutePosition == other.AbsolutePosition && Interval.Equals(other.Interval);
        }

        return Delta == other.Delta && Interval.Equals(other.Interval);
    }

    /// <summary>
    /// Gets the position in this state for a specific time
    /// If there is a temporal function, it evaluates it. Otherwise, uses Delta/AbsolutePosition
    /// </summary>
    /// <param name="time">Time to get the position for</param>
    /// <param name="basePosition">Base position (only used if no temporal function and relative delta)</param>
    /// <returns>Position at the specified time</returns>
    public Point GetPosition(DateTime time, Point? basePosition = null)
    {
        // If there is a temporal function, use it to compute the position
        if (TemporalFunction != null)
        {
            if (!Interval.Contains(time))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(time),
                    $"Time {time} is outside the valid interval {Interval}");
            }

            return TemporalFunction.GetPosition(time);
        }

        // Normal behavior without temporal function
        if (IsAbsolute && AbsolutePosition.HasValue)
        {
            return AbsolutePosition.Value;
        }

        if (basePosition.HasValue)
        {
            return basePosition.Value + Delta;
        }

        return Delta; // If no base, assume Delta is the position
    }

    /// <summary>
    /// Gets the position in this state (without temporal function)
    /// Backward-compatible version for states without temporal functions
    /// </summary>
    public Point GetPosition(Point? basePosition = null)
    {
        // If there is a temporal function but called without time, use interval start
        if (TemporalFunction != null)
        {
            return GetPosition(Interval.Start, basePosition);
        }

        if (IsAbsolute && AbsolutePosition.HasValue)
        {
            return AbsolutePosition.Value;
        }

        if (basePosition.HasValue)
        {
            return basePosition.Value + Delta;
        }

        return Delta;
    }

    public override string ToString()
    {
        if (IsAbsolute && AbsolutePosition.HasValue)
        {
            return $"Abs: {AbsolutePosition.Value} {Interval}";
        }
        return $"Δ: {Delta} {Interval}";
    }
}
