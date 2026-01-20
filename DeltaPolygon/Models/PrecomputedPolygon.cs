namespace DeltaPolygon.Models
{
    /// <summary>
    /// Represents a precomputed reconstruction of a polygon at a specific time.
    /// Provides O(1) access to frequently queried reconstructions.
    /// 
    /// Allows storing reconstructions at specific times for instant access.
    /// </summary>
    public class PrecomputedPolygon
    {
        /// <summary>
        /// ID of the polygon to which this reconstruction belongs
        /// </summary>
        public Guid PolygonId { get; }

        /// <summary>
        /// Time for which this reconstruction was precomputed
        /// </summary>
        public DateTime Time { get; }

        /// <summary>
        /// Precomputed reconstruction of the polygon
        /// </summary>
        public List<Point> Points { get; }

        /// <summary>
        /// Date and time when the precomputation was performed
        /// Useful for validating if the precomputation is still valid
        /// </summary>
        public DateTime PrecomputedAt { get; }

        /// <summary>
        /// Creates a new instance of PrecomputedPolygon
        /// </summary>
        /// <param name="polygonId">Polygon ID</param>
        /// <param name="time">Time for which it was precomputed</param>
        /// <param name="points">List of points in the reconstruction</param>
        public PrecomputedPolygon(Guid polygonId, DateTime time, List<Point> points)
        {
            PolygonId = polygonId;
            Time = time;
            Points = points ?? throw new ArgumentNullException(nameof(points));
            PrecomputedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a new instance of PrecomputedPolygon with a custom precomputed date
        /// </summary>
        /// <param name="polygonId">Polygon ID</param>
        /// <param name="time">Time for which it was precomputed</param>
        /// <param name="points">List of points in the reconstruction</param>
        /// <param name="precomputedAt">Date and time when the precomputation was performed</param>
        public PrecomputedPolygon(Guid polygonId, DateTime time, List<Point> points, DateTime precomputedAt)
        {
            PolygonId = polygonId;
            Time = time;
            Points = points ?? throw new ArgumentNullException(nameof(points));
            PrecomputedAt = precomputedAt;
        }

        /// <summary>
        /// Creates a unique key to identify this precomputation
        /// </summary>
        public (Guid PolygonId, DateTime Time) GetKey() => (PolygonId, Time);

        /// <summary>
        /// Determines if this precomputation is valid for a given polygon and time
        /// </summary>
        public bool IsValidFor(Guid polygonId, DateTime time)
        {
            return PolygonId == polygonId && Time == time;
        }
    }
}
