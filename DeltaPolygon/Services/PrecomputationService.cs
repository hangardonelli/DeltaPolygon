using DeltaPolygon.Models;

namespace DeltaPolygon.Services;

/// <summary>
/// Service for managing temporal polygon precomputations
/// Allows marking specific times for precomputation and storing reconstructions
/// for O(1) access
/// 

/// System for storing reconstructions at specific times, improving
/// performance for repeated queries at the same times.
/// 
/// Thread-safety: This class is thread-safe. Uses ReaderWriterLockSlim for synchronization.
/// </summary>
public class PrecomputationService
{
    /// <summary>
    /// Stores precomputed reconstructions by (PolygonId, Time)
    /// </summary>
    private readonly Dictionary<(Guid PolygonId, DateTime Time), PrecomputedPolygon> _precomputedPolygons;

    /// <summary>
    /// Stores times marked for precomputation by polygon
    /// </summary>
    private readonly Dictionary<Guid, HashSet<DateTime>> _precomputationTimes;

    /// <summary>
    /// Lock for thread-safe synchronization
    /// </summary>
    private readonly ReaderWriterLockSlim _lock;

    /// <summary>
    /// Creates a new instance of PrecomputationService
    /// </summary>
    public PrecomputationService()
    {
        _precomputedPolygons = new Dictionary<(Guid, DateTime), PrecomputedPolygon>();
        _precomputationTimes = new Dictionary<Guid, HashSet<DateTime>>();
        _lock = new ReaderWriterLockSlim();
    }

    /// <summary>
    /// Marks a specific time for precomputation for a polygon
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time to mark for precomputation</param>
    public void MarkForPrecomputation(Guid polygonId, DateTime time)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_precomputationTimes.TryGetValue(polygonId, out var times))
            {
                times = new HashSet<DateTime>();
                _precomputationTimes[polygonId] = times;
            }
            times.Add(time);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Marks multiple times for precomputation for a polygon
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="times">Times to mark for precomputation</param>
    public void MarkForPrecomputation(Guid polygonId, IEnumerable<DateTime> times)
    {
        if (times != null)
        {
            _lock.EnterWriteLock();
            try
            {
                if (!_precomputationTimes.TryGetValue(polygonId, out var existingTimes))
                {
                    existingTimes = new HashSet<DateTime>();
                    _precomputationTimes[polygonId] = existingTimes;
                }

                foreach (var time in times)
                {
                    existingTimes.Add(time);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        else
        {
            throw new ArgumentNullException(nameof(times));
        }
    }

    /// <summary>
    /// Precomputes and stores a reconstruction for a specific polygon and time
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time for which to precompute</param>
    /// <param name="reconstructedPolygon">Polygon reconstruction</param>
    public void Precompute(Guid polygonId, DateTime time, List<Point> reconstructedPolygon)
    {
        if (reconstructedPolygon != null)
        {
            var precomputed = new PrecomputedPolygon(polygonId, time, reconstructedPolygon);
            var key = (polygonId, time);

            _lock.EnterWriteLock();
            try
            {
                _precomputedPolygons[key] = precomputed;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        else
        {
            throw new ArgumentNullException(nameof(reconstructedPolygon));
        }
    }

    /// <summary>
    /// Attempts to get a precomputed reconstruction
    /// Returns true if a precomputation exists for the given polygon and time
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time for which to get the precomputation</param>
    /// <param name="precomputed">Precomputation found, or null if it doesn't exist</param>
    /// <returns>True if a precomputation was found, false otherwise</returns>
    public bool TryGetPrecomputed(Guid polygonId, DateTime time, out PrecomputedPolygon? precomputed)
    {
        var key = (polygonId, time);

        _lock.EnterReadLock();
        try
        {
            if (_precomputedPolygons.TryGetValue(key, out var result))
            {
                precomputed = result;
                return true;
            }

            precomputed = null;
            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets all times marked for precomputation for a polygon
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <returns>Set of times marked for precomputation</returns>
    public IReadOnlySet<DateTime> GetPrecomputationTimes(Guid polygonId)
    {
        _lock.EnterReadLock();
        try
        {
            if (_precomputationTimes.TryGetValue(polygonId, out var times))
            {
                return times.ToHashSet();
            }

            return new HashSet<DateTime>();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Removes the precomputation mark for a specific time
    /// Does not remove the stored precomputation if it exists
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="time">Time to unmark</param>
    public void UnmarkPrecomputation(Guid polygonId, DateTime time)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_precomputationTimes.TryGetValue(polygonId, out var times))
            {
                times.Remove(time);
                if (times.Count == 0)
                {
                    _precomputationTimes.Remove(polygonId);
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Invalidates all precomputations for a specific polygon
    /// Called when the polygon changes (vertex updated, polygon deleted, etc.)
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    public void InvalidatePrecomputations(Guid polygonId)
    {
        _lock.EnterWriteLock();
        try
        {
            // Remove all precomputations for the polygon
            var keysToRemove = _precomputedPolygons.Keys
                .Where(key => key.PolygonId == polygonId)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _precomputedPolygons.Remove(key);
            }

            // Marked times are kept to allow recomputation
            // Only stored reconstructions are cleared
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes all precomputations and marks for a polygon
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    public void ClearPrecomputations(Guid polygonId)
    {
        _lock.EnterWriteLock();
        try
        {
            InvalidatePrecomputations(polygonId);
            _precomputationTimes.Remove(polygonId);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Precomputes all marked reconstructions for a polygon
    /// Uses the provided function to reconstruct the polygon at each time
    /// </summary>
    /// <param name="polygonId">Polygon ID</param>
    /// <param name="reconstructionFunction">Function that reconstructs the polygon for a given time</param>
    public void PrecomputeAllMarked(Guid polygonId, Func<DateTime, List<Point>> reconstructionFunction)
    {
        ArgumentNullException.ThrowIfNull(reconstructionFunction);

        _lock.EnterReadLock();
        HashSet<DateTime>? times;
        try
        {
            if (!_precomputationTimes.TryGetValue(polygonId, out times) || times.Count == 0)
            {
                return;
            }

            // Create copy to not hold the lock during reconstruction
            times = times.ToHashSet();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        // Precompute outside the lock to not block other operations
        foreach (var time in times)
        {
            try
            {
                var reconstructed = reconstructionFunction(time);
                Precompute(polygonId, time, reconstructed);
            }
            catch
            {
                // Ignore errors at individual times
                // Continue with other times
            }
        }
    }

    /// <summary>
    /// Gets the number of stored precomputations
    /// </summary>
    public int PrecomputationCount
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _precomputedPolygons.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets the number of polygons with times marked for precomputation
    /// </summary>
    public int MarkedPolygonsCount
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _precomputationTimes.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Clears all precomputations and marks
    /// </summary>
    public void ClearAll()
    {
        _lock.EnterWriteLock();
        try
        {
            _precomputedPolygons.Clear();
            _precomputationTimes.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Releases resources used by the instance
    /// </summary>
    public void Dispose()
    {
        _lock?.Dispose();
    }
}
