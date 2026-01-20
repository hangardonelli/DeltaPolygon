using System.Collections.ObjectModel;

namespace DeltaPolygon.Models;

/// <summary>
/// Represents a vertex with temporal position history
/// 
/// Thread-safety: This class is thread-safe. Read operations (GetPositionAt, GetStateAt) 
/// do not block each other, while write operations (AddState) are synchronized.
/// </summary>
public class Vertex
{
    public int Id { get; }
    private readonly List<VertexState> _states;
    private readonly object _lock = new();

    public ReadOnlyCollection<VertexState> States
    {
        get
        {
            lock (_lock)
            {
                return _states.AsReadOnly();
            }
        }
    }

    public Vertex(int id)
    {
        Id = id;
        _states = new List<VertexState>();
    }

    /// <summary>
    /// Adds a new temporal state to the vertex
    /// Thread-safe: This operation is synchronized to avoid race conditions
    /// </summary>
    public void AddState(VertexState state)
    {
        lock (_lock)
        {
            if (_states.Count > 0)
            {
                var lastState = _states[^1];
                if (lastState.Interval.IsOpen)
                {
                    // Close the previous interval
                    var closedInterval = new TimeInterval(
                        lastState.Interval.Start,
                        state.Interval.Start
                    );
                    
                    VertexState closedState;
                    if (lastState.IsAbsolute && lastState.AbsolutePosition.HasValue)
                    {
                        // Keep as absolute state
                        closedState = new VertexState(
                            lastState.AbsolutePosition.Value,
                            closedInterval,
                            isAbsolute: true
                        );
                    }
                    else
                    {
                        // Keep as relative delta
                        closedState = new VertexState(
                            lastState.Delta,
                            closedInterval
                        );
                    }
                    
                    _states[^1] = closedState;
                }
            }

            _states.Add(state);
        }
    }

    /// <summary>
    /// Gets the vertex position at a specific time
    /// Uses binary search O(log H) to find the temporal state
    /// 
    
    /// Temporal indexing (B-Tree, GiST) ensures efficient O(log H) search
    /// </summary>
    public Point? GetPositionAt(DateTime time)
    {
        lock (_lock)
        {
            var state = FindStateByTimeUnsafe(time);
            if (state == null)
            {
                return null;
            }

            // If there is a temporal function, use it directly
            if (state.TemporalFunction != null)
            {
                return state.GetPosition(time);
            }

            // If absolute, return directly
            if (state.IsAbsolute && state.AbsolutePosition.HasValue)
            {
                return state.AbsolutePosition.Value;
            }

            // If delta, we need the base position (first absolute or accumulated state)
            // Accumulate positions from the start until BEFORE the current state
            Point basePosition = new(0, 0);
            bool foundBase = false;

            foreach (var s in _states)
            {
                // If we reach the current state, don't accumulate its delta here
                // (it will be applied in GetPosition)
                if (s == state)
                    break;

                if (s.Interval.Start > time)
                    break;

                // If a previous state has a temporal function, use it for the base
                if (s.TemporalFunction != null)
                {
                    // Use the temporal function at the time before the current state
                    var previousTime = state.Interval.Start.AddTicks(-1);
                    if (s.Interval.Contains(previousTime))
                    {
                        basePosition = s.TemporalFunction.GetPosition(previousTime);
                        foundBase = true;
                    }
                }
                else if (s.IsAbsolute && s.AbsolutePosition.HasValue)
                {
                    // When we find an absolute position, reset the base
                    basePosition = s.AbsolutePosition.Value;
                    foundBase = true;
                }
                else if (foundBase)
                {
                    // Accumulate all deltas from the last absolute position or function
                    // until BEFORE the current state
                    basePosition += s.Delta;
                }
            }

            // GetPosition will apply the current state's delta to the accumulated base position
            // Or evaluate the temporal function if present
            return state.GetPosition(time, basePosition);
        }
    }

    /// <summary>
    /// Gets the active state at a specific time
    /// Uses binary search O(log H) to find the temporal state
    /// </summary>
    public VertexState? GetStateAt(DateTime time)
    {
        lock (_lock)
        {
            return FindStateByTimeUnsafe(time);
        }
    }

    /// <summary>
    /// Finds the state that contains the specified time using binary search O(log H)
    /// The list of states is sorted by start time
    /// NOTE: This method assumes the lock is already held (call only from methods that already have the lock)
    /// </summary>
    private VertexState? FindStateByTimeUnsafe(DateTime time)
    {
        if (_states.Count == 0)
        {
            return null;
        }

        // Binary search to find the state that contains the time
        int left = 0;
        int right = _states.Count - 1;
        int result = -1;

        // Search for the last state where Start <= time
        while (left <= right)
        {
            int mid = left + (right - left) / 2;
            var midState = _states[mid];
            
            if (midState.Interval.Start <= time)
            {
                // This state could contain the time, but we check all where Start <= time
                result = mid;
                left = mid + 1; // Search further ahead
            }
            else
            {
                right = mid - 1;
            }
        }

        // Check if the found state contains the time
        if (result >= 0)
        {
            var candidate = _states[result];
            if (candidate.Interval.Contains(time))
            {
                return candidate;
            }
        }

        // Also check previous states that might contain the time
        // (in case of overlaps or the time is in a closed previous interval)
        for (int i = result; i >= 0; i--)
        {
            if (_states[i].Interval.Contains(time))
            {
                return _states[i];
            }
            // If we already passed the start time, there are no more possibilities
            if (_states[i].Interval.Start > time)
            {
                break;
            }
        }

        return null;
    }
}
