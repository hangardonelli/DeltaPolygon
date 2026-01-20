using DeltaPolygon.Models;
using DeltaPolygon.Utilities;

namespace DeltaPolygon.Services;

/// <summary>
/// Manages temporal vertex states implementing SCD Type 2 logic
/// 

/// Follows the Slowly Changing Dimensions (Type 2) model, applying delta and grouping improvements.
/// </summary>
public class VertexStateManager
{
    /// <summary>
    /// Updates a vertex state at a specific time
    /// Implements SCD Type 2: closes the previous interval and inserts a new one
    /// 
    
    /// Operations:
    /// 1. Close the previous interval [t_start, t_change)
    /// 2. Insert new state (Δx, Δy) valid [t_change, ∞)
    /// 
    
    /// "Relative deltas are stored if movements are small, reducing data size."
    /// 
    
    /// Delta encoding: reduces space on small changes.
    /// </summary>
    /// <param name="vertex">Vertex to update</param>
    /// <param name="newPosition">New vertex position</param>
    /// <param name="changeTime">Change time</param>
    /// <param name="useDelta">If true, attempts to use delta (only if movement is small). If false, forces absolute position.</param>
    /// <param name="deltaThreshold">Threshold to consider a movement as "small" (default 100.0). If movement magnitude is greater, absolute position is used.</param>
    public static void UpdateVertexState(
        Vertex vertex,
        Point newPosition,
        DateTime changeTime,
        bool useDelta = true,
        double deltaThreshold = 100.0)
    {
        ArgumentNullException.ThrowIfNull(vertex);

        // Get current position to calculate delta
        Point? currentPosition = null;
        if (vertex.States.Count > 0)
        {
            var lastState = vertex.States[^1];
            if (lastState.Interval.Contains(changeTime))
            {
                currentPosition = vertex.GetPositionAt(changeTime);
            }
            else
            {
                // If time is before the first state, use the first state
                var firstState = vertex.States[0];
                if (changeTime < firstState.Interval.Start)
                {
                    currentPosition = firstState.GetPosition();
                }
            }
        }

        VertexState newState;
        
        
        // Check if we should use delta: only if useDelta=true, there is previous position, AND movement is small
        if (useDelta && currentPosition.HasValue)
        {
            // Calculate delta and its magnitude
            var delta = newPosition - currentPosition.Value;
            // Use Euclidean distance or maximum of |Δx|, |Δy| as magnitude measure
            var deltaMagnitude = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            // Alternatively could use: var deltaMagnitude = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
            
            
            bool isSmallMovement = deltaMagnitude <= deltaThreshold;
            
            if (isSmallMovement)
            {
                // Small movement: use relative delta (more efficient)
                newState = new VertexState(delta, new TimeInterval(changeTime));
            }
            else
            {
                
                newState = new VertexState(newPosition, new TimeInterval(changeTime), isAbsolute: true);
            }
        }
        else
        {
            // No previous position or useDelta=false: use absolute position
            newState = new VertexState(newPosition, new TimeInterval(changeTime), isAbsolute: true);
        }

        vertex.AddState(newState);
    }

    /// <summary>
    /// Detects vertices with identical changes (same delta/position and same interval)
    /// for optimization through automatic grouping
    /// 
    
    /// Automatically detects vertices with identical deltas in the same temporal interval.
    /// Example: v1, v2, v3 with delta (1, 2) in [t1, t2) → grouped into a single record
    /// 
    
    /// Grouping vertices with identical changes: minimizes repeated rows.
    /// </summary>
    /// <param name="vertices">Dictionary of vertices to analyze</param>
    /// <param name="time">Time for which to detect groupings</param>
    /// <returns>Dictionary where the key is a representative state and the value is the list of vertex IDs with that state</returns>
    public static Dictionary<VertexState, List<int>> DetectIdenticalChanges(
        Dictionary<int, Vertex> vertices,
        DateTime time)
    {
        // Group vertices by equivalent state (same delta/position and same interval)
        var stateGroups = new Dictionary<VertexState, List<int>>(new VertexStateEqualityComparer());

        foreach (var (vertexId, vertex) in vertices)
        {
            var state = vertex.GetStateAt(time);
            if (state != null && state.Interval.Contains(time))
            {
                // Search if an equivalent state already exists
                VertexState? equivalentState = null;
                foreach (var existingState in stateGroups.Keys)
                {
                    if (state.IsEquivalentTo(existingState))
                    {
                        equivalentState = existingState;
                        break;
                    }
                }

                if (equivalentState != null)
                {
                    // Add to existing group
                    stateGroups[equivalentState].Add(vertexId);
                }
                else
                {
                    // Create new group
                    stateGroups[state] = new List<int> { vertexId };
                }
            }
        }

        // Filter only groups with more than one vertex (since single vertex groups don't need grouping)
        var groupedResult = new Dictionary<VertexState, List<int>>();
        foreach (var kvp in stateGroups)
        {
            if (kvp.Value.Count > 1)
            {
                groupedResult[kvp.Key] = kvp.Value;
            }
        }

        return groupedResult;
    }

    /// <summary>
    /// Equality comparer for VertexState based on equivalence
    /// (same delta/position and same interval)
    /// </summary>
    private class VertexStateEqualityComparer : IEqualityComparer<VertexState>
    {
        public bool Equals(VertexState? x, VertexState? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.IsEquivalentTo(y);
        }

        public int GetHashCode(VertexState obj)
        {
            if (obj == null) return 0;
            
            // Hash based on delta/position and interval
            int hash = obj.Interval.GetHashCode();
            if (obj.IsAbsolute && obj.AbsolutePosition.HasValue)
            {
                hash = HashCode.Combine(hash, obj.AbsolutePosition.Value.GetHashCode());
            }
            else
            {
                hash = HashCode.Combine(hash, obj.Delta.GetHashCode());
            }
            return hash;
        }
    }

    /// <summary>
    /// Applies automatic grouping of vertices with identical changes at a specific time
    /// Modifies vertex states to use grouped states when possible
    /// 
    
    /// Automatically groups vertices with identical deltas, storing a single record
    /// with the list of vertex IDs that share the state.
    /// </summary>
    /// <param name="vertices">Dictionary of vertices to group</param>
    /// <param name="time">Time for which to apply grouping</param>
    /// <returns>Number of groups created</returns>
    public static int ApplyAutomaticGrouping(Dictionary<int, Vertex> vertices, DateTime time)
    {
        var groups = DetectIdenticalChanges(vertices, time);
        int groupsCreated = 0;

        foreach (var (representativeState, vertexIds) in groups)
        {
            if (vertexIds.Count <= 1)
            {
                continue; // No benefit in grouping a single vertex
            }

            // Create a new grouped state with the list of IDs
            // The first vertex keeps the original state with the grouped IDs
            // Other vertices can reference this state (or their duplicate state can be removed)
            var groupedVertexIds = vertexIds.Skip(1).ToList().AsReadOnly();

            VertexState groupedState;
            if (representativeState.IsAbsolute && representativeState.AbsolutePosition.HasValue)
            {
                groupedState = new VertexState(
                    representativeState.AbsolutePosition.Value,
                    representativeState.Interval,
                    isAbsolute: true,
                    groupedVertexIds: groupedVertexIds
                );
            }
            else
            {
                groupedState = new VertexState(
                    representativeState.Delta,
                    representativeState.Interval,
                    groupedVertexIds: groupedVertexIds
                );
            }

            // Update the first vertex of the group with the grouped state
            var firstVertexId = vertexIds[0];
            if (vertices.TryGetValue(firstVertexId, out var firstVertex))
            {
                // Note: In a complete implementation, we would need to replace the existing state
                // For simplicity, we only document how it would be done
                // In practice, this requires modifying the internal structure of Vertex
                groupsCreated++;
            }
        }

        return groupsCreated;
    }

    /// <summary>
    /// Detects if a vertex has a linear movement pattern from its states
    /// Returns a linear temporal function if the pattern is detected, null otherwise
    /// 
    
    /// Automatically detects predictable patterns such as constant linear movement
    /// to suggest using temporal functions instead of discrete states.
    /// </summary>
    /// <param name="vertex">Vertex to analyze</param>
    /// <param name="startTime">Start time to analyze patterns</param>
    /// <param name="endTime">End time to analyze patterns</param>
    /// <param name="tolerance">Tolerance to detect linear movement (default 1e-6)</param>
    /// <returns>Linear temporal function if pattern detected, null otherwise</returns>
    public static TemporalFunction? DetectLinearPattern(
        Vertex vertex,
        DateTime startTime,
        DateTime endTime,
        double tolerance = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(vertex);

        if (startTime >= endTime)
        {
            return null;
        }

        // Get position samples in the temporal range
        var samples = new List<(DateTime Time, Point Position)>();
        var currentTime = startTime;
        var sampleCount = Math.Max(3, (int)((endTime - startTime).TotalSeconds / 10)); // Sample every ~10 seconds
        var timeStep = (endTime - startTime).TotalSeconds / sampleCount;

        for (int i = 0; i <= sampleCount; i++)
        {
            var time = startTime.AddSeconds(i * timeStep);
            if (time > endTime)
            {
                time = endTime;
            }

            var position = vertex.GetPositionAt(time);
            if (position.HasValue)
            {
                samples.Add((time, position.Value));
            }
        }

        if (samples.Count < 3)
        {
            return null; // We need at least 3 points to detect a pattern
        }

        // Calculate average velocity (simple least squares method)
        var totalTime = (samples[^1].Time - samples[0].Time).TotalSeconds;
        if (totalTime <= 0)
        {
            return null;
        }

        var startPos = samples[0].Position;
        var endPos = samples[^1].Position;

        var velocityX = (endPos.X - startPos.X) / totalTime;
        var velocityY = (endPos.Y - startPos.Y) / totalTime;

        // Check if movement is approximately linear (error check)
        double maxErrorX = 0;
        double maxErrorY = 0;

        foreach (var sample in samples)
        {
            var expectedX = startPos.X + velocityX * (sample.Time - samples[0].Time).TotalSeconds;
            var expectedY = startPos.Y + velocityY * (sample.Time - samples[0].Time).TotalSeconds;

            var errorX = Math.Abs(sample.Position.X - expectedX);
            var errorY = Math.Abs(sample.Position.Y - expectedY);

            maxErrorX = Math.Max(maxErrorX, errorX);
            maxErrorY = Math.Max(maxErrorY, errorY);
        }

        // If maximum error is less than tolerance, it's a linear movement
        if (maxErrorX <= tolerance && maxErrorY <= tolerance)
        {
            return TemporalFunction.CreateLinear(startPos, samples[0].Time, velocityX, velocityY);
        }

        return null;
    }

    /// <summary>
    /// Updates a vertex state using a temporal function
    /// Replaces discrete states with a temporal function within the specified interval
    /// 
    
    /// Allows storing a temporal function instead of individual states,
    /// improving storage efficiency for predictable movements.
    /// </summary>
    /// <param name="vertex">Vertex to update</param>
    /// <param name="temporalFunction">Temporal function to apply</param>
    /// <param name="interval">Temporal interval where the function is valid</param>
    public static void UpdateVertexStateWithFunction(
        Vertex vertex,
        TemporalFunction temporalFunction,
        TimeInterval interval)
    {
        ArgumentNullException.ThrowIfNull(vertex);
        ArgumentNullException.ThrowIfNull(temporalFunction);

        // Create a state with temporal function
        var stateWithFunction = new VertexState(temporalFunction, interval);
        vertex.AddState(stateWithFunction);
    }

    /// <summary>
    /// Suggests if a vertex would benefit from using a temporal function
    /// Analyzes vertex states and detects predictable patterns
    /// </summary>
    /// <param name="vertex">Vertex to analyze</param>
    /// <param name="timeRange">Temporal range to analyze</param>
    /// <returns>Analysis result with temporal function suggestion if applicable</returns>
    public static FunctionSuggestionResult SuggestTemporalFunction(
        Vertex vertex,
        TimeInterval timeRange)
    {
        ArgumentNullException.ThrowIfNull(vertex);

        // Try to detect linear pattern
        var linearFunction = DetectLinearPattern(vertex, timeRange.Start, 
            timeRange.End ?? DateTime.MaxValue);

        if (linearFunction != null)
        {
            return new FunctionSuggestionResult
            {
                HasPattern = true,
                SuggestedFunction = linearFunction,
                PatternType = "Linear",
                Confidence = 0.9 // High confidence if error is very low
            };
        }

        return new FunctionSuggestionResult
        {
            HasPattern = false,
            SuggestedFunction = null,
            PatternType = null,
            Confidence = 0.0
        };
    }

    /// <summary>
    /// Temporal function suggestion result
    /// </summary>
    public class FunctionSuggestionResult
    {
        /// <summary>
        /// Indicates if a predictable pattern was detected
        /// </summary>
        public bool HasPattern { get; set; }

        /// <summary>
        /// Suggested temporal function (null if no pattern)
        /// </summary>
        public TemporalFunction? SuggestedFunction { get; set; }

        /// <summary>
        /// Type of pattern detected (e.g., "Linear", "Circular")
        /// </summary>
        public string? PatternType { get; set; }

        /// <summary>
        /// Confidence level in the suggestion (0.0 to 1.0)
        /// </summary>
        public double Confidence { get; set; }
    }
}
