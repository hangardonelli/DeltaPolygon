using DeltaPolygon.Models;
using DeltaPolygon.Utilities;
using DeltaPolygon.Validators;
using DeltaPolygon.Events;
using DeltaPolygon.Coordinates;

namespace DeltaPolygon.Services;

/// <summary>
/// Main class for creating and manipulating temporal polygons
/// 

/// Implements separation between immutable topological structure and temporal vertex states
/// 
/// Thread-safety: This class is thread-safe. Uses ReaderWriterLockSlim to allow multiple concurrent reads
/// and synchronize writes. Read operations do not block each other, improving performance.
/// </summary>
public class PolygonDelta
{
    private readonly Dictionary<Guid, TemporalPolygon> _polygons;
    private readonly ReaderWriterLockSlim _lock;
    private readonly CacheManager<(Guid PolygonId, DateTime Time), List<Point>> _reconstructionCache;
    private readonly PrecomputationService _precomputationService;
    
    /// <summary>
    /// Inverse index to track cache entries by polygon
    /// Allows invalidating only entries for a specific polygon instead of clearing the entire cache
    /// </summary>
    private readonly Dictionary<Guid, HashSet<(Guid PolygonId, DateTime Time)>> _cacheIndex;
    private readonly object _cacheIndexLock = new();

    /// <summary>
    /// Event that fires when a polygon changes (created, updated, deleted)
    /// </summary>
    public event EventHandler<PolygonChangedEventArgs>? PolygonChanged;

    /// <summary>
    /// Event that fires when a vertex changes
    /// </summary>
    public event EventHandler<VertexChangedEventArgs>? VertexChanged;

    /// <summary>
    /// Creates a new instance of PolygonDelta
    /// </summary>
    /// <param name="cacheMaxSize">Maximum size of the LRU cache (default 100)</param>
    public PolygonDelta(int cacheMaxSize = 100)
    {
        _polygons = new Dictionary<Guid, TemporalPolygon>();
        _lock = new ReaderWriterLockSlim();
        _reconstructionCache = new CacheManager<(Guid PolygonId, DateTime Time), List<Point>>(cacheMaxSize);
        _precomputationService = new PrecomputationService();
        _cacheIndex = new Dictionary<Guid, HashSet<(Guid PolygonId, DateTime Time)>>();
    }

    /// <summary>
    /// Creates a new temporal polygon with initial vertices
    /// 
    
    /// Represents P as an ordered sequence of vertex identifiers:
    /// P_struct = ⟨id_v1, id_v2, ..., id_vn⟩
    /// The topology is stored once as long as the vertices do not change.
    /// </summary>
    /// <param name="initialVertices">Initial vertices of the polygon</param>
    /// <param name="initialTime">Initial time for vertex states</param>
    /// <param name="coordinateSystem">Polygon coordinate system (default Cartesian)</param>
    public TemporalPolygon CreatePolygon(IEnumerable<Point> initialVertices, DateTime initialTime, CoordinateSystem coordinateSystem = CoordinateSystem.Cartesian)
    {
        ArgumentNullException.ThrowIfNull(initialVertices);

        var verticesList = initialVertices.ToList();
        
        // Basic geometric validation
        var validationResult = PolygonValidator.Validate(verticesList);
        if (!validationResult.IsValid)
        {
            var errorMessage = string.Join("; ", validationResult.Errors);
            throw new ArgumentException($"Invalid polygon: {errorMessage}", nameof(initialVertices));
        }

        var polygonId = Guid.NewGuid();
        var vertices = new Dictionary<int, Vertex>();
        var vertexIds = new List<int>();

        // Create vertices with initial state
        for (int i = 0; i < verticesList.Count; i++)
        {
            var vertexId = i;
            var vertex = new Vertex(vertexId);
            var initialState = new VertexState(
                verticesList[i],
                new TimeInterval(initialTime),
                isAbsolute: true
            );
            vertex.AddState(initialState);
            vertices[vertexId] = vertex;
            vertexIds.Add(vertexId);
        }

        var polygon = new TemporalPolygon(polygonId, vertexIds, vertices, coordinateSystem);
        
        _lock.EnterWriteLock();
        try
        {
            _polygons[polygonId] = polygon;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        // Invalidate cache and precomputations, fire event
        InvalidateCacheForPolygon(polygonId);
        _precomputationService.InvalidatePrecomputations(polygonId);
        OnPolygonChanged(polygonId, PolygonChangeType.Created, polygon);

        return polygon;
    }

    /// <summary>
    /// Updates a vertex position at a specific time
    /// 
    
    /// Each vertex has a position history: S(v) = {(Δx_k, Δy_k, [t_start, t_end))}_k
    /// Relative deltas are stored if movements are small, reducing data size.
    /// 
    
    /// Implements SCD Type 2: closes the previous interval and inserts a new one.
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="vertexId">Vertex ID</param>
    /// <param name="newPosition">New vertex position</param>
    /// <param name="changeTime">Change time</param>
    /// <param name="useDelta">If true, attempts to use delta (only if movement is small). If false, forces absolute position.</param>
    /// <param name="deltaThreshold">Threshold to consider a movement as "small" (default 100.0). If movement magnitude is greater, absolute position is used.</param>
    public void UpdateVertex(Guid polygonId, int vertexId, Point newPosition, DateTime changeTime, bool useDelta = true, double deltaThreshold = 100.0)
    {
        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        var vertex = polygon.GetVertex(vertexId) ?? throw new ArgumentException($"Vertex with ID {vertexId} not found in polygon", nameof(vertexId));
        VertexStateManager.UpdateVertexState(vertex, newPosition, changeTime, useDelta, deltaThreshold);
        
        // Invalidate cache and precomputations, fire event
        InvalidateCacheForPolygon(polygonId);
        _precomputationService.InvalidatePrecomputations(polygonId);
        OnVertexChanged(polygonId, vertexId, changeTime, newPosition);
        OnPolygonChanged(polygonId, PolygonChangeType.VertexChanged, polygon);
    }

    /// <summary>
    /// Gets a polygon by its ID
    /// </summary>
    public TemporalPolygon? GetPolygon(Guid polygonId)
    {
        _lock.EnterReadLock();
        try
        {
            return _polygons.TryGetValue(polygonId, out var polygon) ? polygon : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Reconstructs a polygon at a specific time
    /// Uses precomputations (O(1)), LRU cache, and reconstruction in that order
    /// 
    
    /// P_render(t_target) = ∪_{i=1}^{n} Pos(v_i, t_target)
    /// Reconstructs the complete polygon from all vertex positions at the specified time.
    /// 
    
    /// First checks if a precomputation exists for O(1) access.
    /// </summary>
    public List<Point> GetPolygonAt(Guid polygonId, DateTime time)
    {
        // 1. Try to get from precomputations (O(1))
        if (_precomputationService.TryGetPrecomputed(polygonId, time, out var precomputed))
        {
            return precomputed!.Points.ToList(); // Create copy to avoid modifications
        }

        // 2. Try to get from LRU cache
        var cacheKey = (polygonId, time);
        if (_reconstructionCache.TryGetValue(cacheKey, out var cached))
        {
            return cached!;
        }

        // 3. Reconstruct
        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        var result = TemporalQueryEngine.ReconstructPolygon(polygon, time);
        
        // Save to LRU cache
        _reconstructionCache.Set(cacheKey, result);
        
        // Update inverse index
        UpdateCacheIndex(polygonId, cacheKey);
        
        return result;
    }

    /// <summary>
    /// Gets the position of a specific vertex at a given time
    /// 
    
    /// Pos(v, t_target) = (x, y) ⟺ ∃k : t_start_k ≤ t_target < t_end_k
    /// If delta is used: cumulative sum is applied over the last absolute state.
    /// </summary>
    public Point? GetVertexPosition(Guid polygonId, int vertexId, DateTime time)
    {
        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return TemporalQueryEngine.GetVertexPosition(polygon, vertexId, time);
    }

    /// <summary>
    /// Gets all stored polygons
    /// </summary>
    public IEnumerable<TemporalPolygon> GetAllPolygons()
    {
        _lock.EnterReadLock();
        try
        {
            // Create copy to avoid concurrency issues
            return _polygons.Values.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Removes a polygon
    /// </summary>
    public bool RemovePolygon(Guid polygonId)
    {
        bool removed;
        _lock.EnterWriteLock();
        try
        {
            removed = _polygons.Remove(polygonId);
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (removed)
        {
            // Invalidate cache and precomputations, fire event
            InvalidateCacheForPolygon(polygonId);
            _precomputationService.ClearPrecomputations(polygonId);
            OnPolygonChanged(polygonId, PolygonChangeType.Deleted);
        }

        return removed;
    }

    /// <summary>
    /// Converts a polygon to GeoJSON format at a specific time
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time to reconstruct the polygon</param>
    /// <param name="asFeature">If true, returns a Feature; if false, returns only the Geometry</param>
    /// <returns>JSON string in GeoJSON format</returns>
    public string ToGeoJson(Guid polygonId, DateTime time, bool asFeature = true)
    {
        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return GeoJsonConverter.ToGeoJson(polygon, time, asFeature);
    }

    /// <summary>
    /// Gets all polygons that have valid states at some point within a temporal range
    /// 
    
    /// Allows finding polygons that exist at any moment within a specific temporal range.
    /// </summary>
    /// <param name="startTime">Range start time</param>
    /// <param name="endTime">Range end time</param>
    /// <returns>Polygons that exist at some point within the specified temporal range</returns>
    public IEnumerable<TemporalPolygon> GetPolygonsInTimeRange(DateTime startTime, DateTime endTime)
    {
        if (startTime > endTime)
        {
            throw new ArgumentException("Start time must be less than or equal to end time", nameof(startTime));
        }

        _lock.EnterReadLock();
        try
        {
            var result = new List<TemporalPolygon>();
            foreach (var polygon in _polygons.Values)
            {
                // Check if polygon exists at some point in the range
                if (TemporalQueryEngine.PolygonExistsInRange(polygon, startTime, endTime))
                {
                    result.Add(polygon);
                }
            }
            return result;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all polygons that exist throughout an entire temporal range
    /// </summary>
    /// <param name="startTime">Range start time</param>
    /// <param name="endTime">Range end time</param>
    /// <returns>Polygons that exist throughout the specified temporal range</returns>
    public IEnumerable<TemporalPolygon> GetPolygonsForEntireTimeRange(DateTime startTime, DateTime endTime)
    {
        if (startTime > endTime)
        {
            throw new ArgumentException("Start time must be less than or equal to end time", nameof(startTime));
        }

        _lock.EnterReadLock();
        try
        {
            var result = new List<TemporalPolygon>();
            foreach (var polygon in _polygons.Values)
            {
                if (TemporalQueryEngine.PolygonExistsForEntireRange(polygon, startTime, endTime))
                {
                    result.Add(polygon);
                }
            }
            return result;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the history of a polygon in a temporal range
    /// Returns multiple polygon reconstructions at regular intervals or at state change times
    /// 
    
    /// Allows obtaining the complete change history of a polygon within a temporal range.
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="startTime">Range start time</param>
    /// <param name="endTime">Range end time</param>
    /// <param name="interval">Interval between samples (optional, default at each state change)</param>
    /// <returns>Polygon history in the temporal range: tuples (Time, Reconstruction)</returns>
    public IEnumerable<(DateTime Time, List<Point> Polygon)> GetPolygonHistory(
        Guid polygonId, 
        DateTime startTime, 
        DateTime endTime,
        TimeSpan? interval = null)
    {
        if (startTime > endTime)
        {
            throw new ArgumentException("Start time must be less than or equal to end time", nameof(startTime));
        }

        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        var history = new List<(DateTime, List<Point>)>();

        if (interval.HasValue)
        {
            // Sample at regular intervals
            for (var time = startTime; time <= endTime; time = time.Add(interval.Value))
            {
                try
                {
                    var points = GetPolygonAt(polygonId, time); // Use GetPolygonAt to leverage cache and precomputations
                    history.Add((time, points));
                }
                catch
                {
                    // Skip times without valid state
                }
            }
        }
        else
        {
            // Get at state change times using TemporalQueryEngine
            var changeTimes = TemporalQueryEngine.GetChangeTimesInRange(polygon, startTime, endTime);
            
            foreach (var time in changeTimes)
            {
                try
                {
                    var points = GetPolygonAt(polygonId, time); // Use GetPolygonAt to leverage cache and precomputations
                    history.Add((time, points));
                }
                catch
                {
                    // Skip times without valid state
                }
            }
        }

        return history;
    }

    /// <summary>
    /// Marks a specific time for precomputation for a polygon
    /// Precomputations allow O(1) access to frequently queried reconstructions
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time to mark for precomputation</param>
    public void MarkTimeForPrecomputation(Guid polygonId, DateTime time)
    {
        _precomputationService.MarkForPrecomputation(polygonId, time);
    }

    /// <summary>
    /// Marks multiple times for precomputation for a polygon
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="times">Times to mark for precomputation</param>
    public void MarkTimesForPrecomputation(Guid polygonId, IEnumerable<DateTime> times)
    {
        _precomputationService.MarkForPrecomputation(polygonId, times);
    }

    /// <summary>
    /// Precomputes all marked reconstructions for a polygon
    /// This improves performance for repeated queries at the same times
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    public void PrecomputeMarkedTimes(Guid polygonId)
    {
        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        _precomputationService.PrecomputeAllMarked(polygonId, time => 
        {
            return TemporalQueryEngine.ReconstructPolygon(polygon, time);
        });
    }

    /// <summary>
    /// Precomputes and stores a reconstruction for a specific polygon and time
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time for which to precompute</param>
    public void PrecomputePolygonAt(Guid polygonId, DateTime time)
    {
        var reconstructed = GetPolygonAt(polygonId, time);
        _precomputationService.Precompute(polygonId, time, reconstructed);
    }

    /// <summary>
    /// Removes the precomputation mark for a specific time
    /// Does not remove the stored precomputation if it exists
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time to unmark</param>
    public void UnmarkPrecomputationTime(Guid polygonId, DateTime time)
    {
        _precomputationService.UnmarkPrecomputation(polygonId, time);
    }

    /// <summary>
    /// Gets all times marked for precomputation for a polygon
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <returns>Set of times marked for precomputation</returns>
    public IReadOnlySet<DateTime> GetPrecomputationTimes(Guid polygonId)
    {
        return _precomputationService.GetPrecomputationTimes(polygonId);
    }

    /// <summary>
    /// Removes all precomputations and marks for a polygon
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    public void ClearPrecomputations(Guid polygonId)
    {
        _precomputationService.ClearPrecomputations(polygonId);
    }

    /// <summary>
    /// Updates multiple vertices with the same change (delta or absolute position)
    
    /// 
    
    /// "Consecutive identical validity intervals can be grouped for vertices with equal movements"
    /// 
    
    /// "Grouping vertices with identical changes: minimizes repeated rows"
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="vertexIds">List of vertex IDs to update with the same change</param>
    /// <param name="delta">Delta to apply to all vertices</param>
    /// <param name="changeTime">Change time</param>
    public void UpdateVerticesWithSameDelta(Guid polygonId, IEnumerable<int> vertexIds, Point delta, DateTime changeTime)
    {
        var vertexIdList = vertexIds.ToList();
        if (vertexIdList.Count == 0)
        {
            return;
        }

        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Create the grouped state with all IDs (except the first one that contains it)
        var groupedIds = vertexIdList.Skip(1).ToList().AsReadOnly();
        var interval = new TimeInterval(changeTime);
        
        // The first vertex contains the state with the list of grouped IDs
        var firstVertexId = vertexIdList[0];
        var firstVertex = polygon.GetVertex(firstVertexId) 
            ?? throw new ArgumentException($"Vertex with ID {firstVertexId} not found", nameof(vertexIds));
        
        var groupedState = new VertexState(delta, interval, groupedIds.Count > 0 ? groupedIds : null);
        firstVertex.AddState(groupedState);

        // The other vertices also receive the state (without the grouped list, to avoid redundancy)
        foreach (var vertexId in vertexIdList.Skip(1))
        {
            var vertex = polygon.GetVertex(vertexId);
            if (vertex != null)
            {
                var state = new VertexState(delta, interval);
                vertex.AddState(state);
            }
        }

        // Invalidate cache and precomputations
        InvalidateCacheForPolygon(polygonId);
        _precomputationService.InvalidatePrecomputations(polygonId);
        
        // Fire events for each vertex
        foreach (var vertexId in vertexIdList)
        {
            var vertex = polygon.GetVertex(vertexId);
            if (vertex != null)
            {
                var newPosition = vertex.GetPositionAt(changeTime);
                if (newPosition.HasValue)
                {
                    OnVertexChanged(polygonId, vertexId, changeTime, newPosition.Value);
                }
            }
        }
        OnPolygonChanged(polygonId, PolygonChangeType.VertexChanged, polygon);
    }

    /// <summary>
    /// Detects vertices with identical changes in a polygon for a given time
    /// Useful for analysis and optimization
    /// 
    
    /// "Automatically detects vertices with identical deltas in the same temporal interval"
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time for which to detect groupings</param>
    /// <returns>Dictionary where the key is a representative state and the value is the list of vertex IDs with that state</returns>
    public Dictionary<VertexState, List<int>> DetectIdenticalChanges(Guid polygonId, DateTime time)
    {
        _lock.EnterReadLock();
        TemporalPolygon? polygon;
        try
        {
            if (!_polygons.TryGetValue(polygonId, out polygon))
            {
                throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Use VertexStateManager to detect identical changes
        var vertices = polygon.Vertices.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        return VertexStateManager.DetectIdenticalChanges(vertices, time);
    }

    /// <summary>
    /// Invalidates the cache for a specific polygon
    /// Uses an inverse index to remove only entries related to the polygon,
    /// improving efficiency in scenarios with multiple polygons
    /// </summary>
    private void InvalidateCacheForPolygon(Guid polygonId)
    {
        lock (_cacheIndexLock)
        {
            // Get all cache entries for this polygon from the inverse index
            if (_cacheIndex.TryGetValue(polygonId, out var cacheKeys))
            {
                // Remove each cache entry
                foreach (var cacheKey in cacheKeys)
                {
                    _reconstructionCache.Remove(cacheKey);
                }
                
                // Clear the index for this polygon
                _cacheIndex.Remove(polygonId);
            }
        }
    }

    /// <summary>
    /// Updates the cache inverse index when a new entry is added
    /// </summary>
    private void UpdateCacheIndex(Guid polygonId, (Guid PolygonId, DateTime Time) cacheKey)
    {
        lock (_cacheIndexLock)
        {
            if (!_cacheIndex.TryGetValue(polygonId, out var cacheKeys))
            {
                cacheKeys = new HashSet<(Guid PolygonId, DateTime Time)>();
                _cacheIndex[polygonId] = cacheKeys;
            }
            
            cacheKeys.Add(cacheKey);
        }
    }

    /// <summary>
    /// Fires the PolygonChanged event
    /// </summary>
    protected virtual void OnPolygonChanged(Guid polygonId, PolygonChangeType changeType, TemporalPolygon? polygon = null)
    {
        PolygonChanged?.Invoke(this, new PolygonChangedEventArgs(polygonId, changeType, polygon));
    }

    /// <summary>
    /// Fires the VertexChanged event
    /// </summary>
    protected virtual void OnVertexChanged(Guid polygonId, int vertexId, DateTime changeTime, Point newPosition)
    {
        VertexChanged?.Invoke(this, new VertexChangedEventArgs(polygonId, vertexId, changeTime, newPosition));
    }

    /// <summary>
    /// Releases resources used by the instance
    /// </summary>
    public void Dispose()
    {
        _lock?.Dispose();
    }
}
