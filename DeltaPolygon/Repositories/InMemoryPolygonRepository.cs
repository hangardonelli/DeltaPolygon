using DeltaPolygon.Models;
using DeltaPolygon.Services;

namespace DeltaPolygon.Repositories
{
    /// <summary>
    /// In-memory implementation of the polygon repository.
    /// Useful for testing and simple usage without persistence.
    /// 
    /// Thread-safety: This class is thread-safe. All operations are synchronized.
    /// </summary>
    public class InMemoryPolygonRepository : IPolygonRepository
    {
        private readonly Dictionary<Guid, TemporalPolygon> _storage;
        private readonly object _lock = new();

        public InMemoryPolygonRepository()
        {
            _storage = new Dictionary<Guid, TemporalPolygon>();
        }

        public Task SaveAsync(TemporalPolygon polygon)
        {
            ArgumentNullException.ThrowIfNull(polygon, nameof(polygon));

            lock (_lock)
            {
                _storage[polygon.Id] = polygon;
            }
            return Task.CompletedTask;
        }

        public Task<TemporalPolygon?> LoadAsync(Guid polygonId)
        {
            lock (_lock)
            {
                _storage.TryGetValue(polygonId, out var polygon);
                return Task.FromResult(polygon);
            }
        }

        public Task<List<Point>?> QueryAtTimeAsync(Guid polygonId, DateTime time)
        {
            TemporalPolygon? polygon;
            lock (_lock)
            {
                if (!_storage.TryGetValue(polygonId, out polygon))
                {
                    return Task.FromResult<List<Point>?>(null);
                }
            }

            try
            {
                var points = polygon.ReconstructAt(time);
                return Task.FromResult<List<Point>?>(points);
            }
            catch
            {
                return Task.FromResult<List<Point>?>(null);
            }
        }

        public Task<IEnumerable<Guid>> GetAllPolygonIdsAsync()
        {
            lock (_lock)
            {
                return Task.FromResult<IEnumerable<Guid>>(_storage.Keys.ToList());
            }
        }

        public Task<bool> DeleteAsync(Guid polygonId)
        {
            lock (_lock)
            {
                return Task.FromResult(_storage.Remove(polygonId));
            }
        }

        public Task<IEnumerable<TemporalPolygon>> GetPolygonsInTimeRangeAsync(DateTime startTime, DateTime endTime)
        {
            if (startTime > endTime)
            {
                throw new ArgumentException("Start time must be less than or equal to end time", nameof(startTime));
            }

            lock (_lock)
            {
                var result = new List<TemporalPolygon>();
                foreach (var polygon in _storage.Values)
                {
                    if (TemporalQueryEngine.PolygonExistsInRange(polygon, startTime, endTime))
                    {
                        result.Add(polygon);
                    }
                }
                return Task.FromResult<IEnumerable<TemporalPolygon>>(result);
            }
        }

        public Task<IEnumerable<(DateTime Time, List<Point> Polygon)>> GetPolygonHistoryAsync(
            Guid polygonId,
            DateTime startTime,
            DateTime endTime,
            TimeSpan? interval = null)
        {
            if (startTime > endTime)
            {
                throw new ArgumentException("Start time must be less than or equal to end time", nameof(startTime));
            }

            TemporalPolygon? polygon;
            lock (_lock)
            {
                if (!_storage.TryGetValue(polygonId, out polygon))
                {
                    throw new ArgumentException($"Polygon with ID {polygonId} not found", nameof(polygonId));
                }
            }

            var history = new List<(DateTime, List<Point>)>();

            if (interval.HasValue)
            {
                // Sample at regular intervals
                for (var time = startTime; time <= endTime; time = time.Add(interval.Value))
                {
                    try
                    {
                        var points = polygon.ReconstructAt(time);
                        history.Add((time, points));
                    }
                    catch
                    {
                        // Skip times with no valid state
                    }
                }
            }
            else
            {
                // Get times of state changes
                var changeTimes = TemporalQueryEngine.GetChangeTimesInRange(polygon, startTime, endTime);

                foreach (var time in changeTimes)
                {
                    try
                    {
                        var points = polygon.ReconstructAt(time);
                        history.Add((time, points));
                    }
                    catch
                    {
                        // Skip times with no valid state
                    }
                }
            }

            return Task.FromResult<IEnumerable<(DateTime, List<Point>)>>(history);
        }

        /// <summary>
        /// Clears all storage (useful for testing)
        /// Thread-safe: This operation is synchronized
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _storage.Clear();
            }
        }
    }
}
