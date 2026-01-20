using DeltaPolygon.Models;

namespace DeltaPolygon.Repositories
{
    /// <summary>
    /// Interface for persistence of temporal polygons
    /// </summary>
    public interface IPolygonRepository
    {
        /// <summary>
        /// Saves a temporal polygon
        /// </summary>
        Task SaveAsync(TemporalPolygon polygon);

        /// <summary>
        /// Loads a polygon by its ID
        /// </summary>
        Task<TemporalPolygon?> LoadAsync(Guid polygonId);

        /// <summary>
        /// Queries a polygon at a specific time
        /// </summary>
        Task<List<Point>?> QueryAtTimeAsync(Guid polygonId, DateTime time);

        /// <summary>
        /// Gets all stored polygon IDs
        /// </summary>
        Task<IEnumerable<Guid>> GetAllPolygonIdsAsync();

        /// <summary>
        /// Deletes a polygon
        /// </summary>
        Task<bool> DeleteAsync(Guid polygonId);

        /// <summary>
        /// Gets all polygons that exist at any point within a given time range
        /// </summary>
        /// <param name="startTime">Start time of the range</param>
        /// <param name="endTime">End time of the range</param>
        /// <returns>Polygons that exist at any time within the range</returns>
        Task<IEnumerable<TemporalPolygon>> GetPolygonsInTimeRangeAsync(DateTime startTime, DateTime endTime);

        /// <summary>
        /// Gets the history of a polygon within a time range.
        /// Returns multiple reconstructions at change times or at regular intervals.
        /// </summary>
        /// <param name="polygonId">Polygon ID</param>
        /// <param name="startTime">Start time of the range</param>
        /// <param name="endTime">End time of the range</param>
        /// <param name="interval">Sampling interval (optional, null for change times)</param>
        /// <returns>Polygon history: tuples of (Time, Reconstruction)</returns>
        Task<IEnumerable<(DateTime Time, List<Point> Polygon)>> GetPolygonHistoryAsync(
            Guid polygonId,
            DateTime startTime,
            DateTime endTime,
            TimeSpan? interval = null);
    }
}
