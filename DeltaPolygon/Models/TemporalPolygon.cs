using System.Collections.ObjectModel;
using DeltaPolygon.Utilities;
using DeltaPolygon.Serialization;
using DeltaPolygon.Coordinates;

namespace DeltaPolygon.Models;

/// <summary>
/// Represents a temporal polygon with immutable topological structure
/// and temporal vertex states
/// </summary>
public class TemporalPolygon
{
    public Guid Id { get; }
    
    /// <summary>
    /// Immutable topological structure: ordered sequence of vertex IDs
    /// </summary>
    public ReadOnlyCollection<int> VertexIds { get; }
    
    /// <summary>
    /// Coordinate system in which the polygon vertices are defined
    /// Default is Cartesian
    /// </summary>
    public CoordinateSystem CoordinateSystem { get; }
    
    /// <summary>
    /// Dictionary of vertices by ID
    /// </summary>
    private readonly Dictionary<int, Vertex> _vertices;

    public TemporalPolygon(Guid id, IEnumerable<int> vertexIds, Dictionary<int, Vertex> vertices, CoordinateSystem coordinateSystem = CoordinateSystem.Cartesian)
    {
        Id = id;
        VertexIds = new ReadOnlyCollection<int>(vertexIds.ToList());
        _vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        CoordinateSystem = coordinateSystem;
    }

    /// <summary>
    /// Gets a vertex by its ID
    /// </summary>
    public Vertex? GetVertex(int vertexId)
    {
        return _vertices.TryGetValue(vertexId, out var vertex) ? vertex : null;
    }

    /// <summary>
    /// Gets all vertices
    /// </summary>
    public IReadOnlyDictionary<int, Vertex> Vertices => _vertices;

    /// <summary>
    /// Reconstructs the polygon at a specific time
    /// Returns a list of points ordered according to the topological structure
    /// 
    
    /// P_render(t_target) = ∪_{i=1}^{n} Pos(v_i, t_target)
    /// </summary>
    public List<Point> ReconstructAt(DateTime time)
    {
        var points = new List<Point>();

        foreach (var vertexId in VertexIds)
        {
            var vertex = GetVertex(vertexId) ?? throw new InvalidOperationException($"Vértice con ID {vertexId} no encontrado");
            var position = vertex.GetPositionAt(time);
            if (!position.HasValue)
            {
                throw new InvalidOperationException($"No position found for vertex {vertexId} at time {time}");
            }

            points.Add(position.Value);
        }

        return points;
    }

    /// <summary>
    /// Gets grouping statistics for a specific time
    /// Useful for storage efficiency analysis
    /// 
    
    /// "Grouping and range encoding reduce rows by orders of magnitude"
    /// </summary>
    /// <param name="time">Time to analyze</param>
    /// <returns>Tuple with (totalVertices, groupedVertices, uniqueGroups)</returns>
    public (int TotalVertices, int GroupedVertices, int UniqueGroups) GetGroupingStats(DateTime time)
    {
        int totalVertices = VertexIds.Count;
        int groupedVertices = 0;
        var uniqueStates = new HashSet<(Point Delta, bool IsAbsolute, Point? AbsolutePosition, DateTime Start, DateTime? End)>();

        foreach (var vertexId in VertexIds)
        {
            var vertex = GetVertex(vertexId);
            if (vertex == null) continue;

            var state = vertex.GetStateAt(time);
            if (state == null) continue;

            // Count grouped vertices
            if (state.IsGrouped && state.GroupedVertexIds != null)
            {
                groupedVertices += state.GroupedVertexIds.Count;
            }

            // Add unique state
            var stateKey = (
                state.Delta, 
                state.IsAbsolute, 
                state.AbsolutePosition, 
                state.Interval.Start, 
                state.Interval.End
            );
            uniqueStates.Add(stateKey);
        }

        return (totalVertices, groupedVertices, uniqueStates.Count);
    }

    /// <summary>
    /// Validates that the polygon has at least 3 vertices
    /// </summary>
    public bool IsValid()
    {
        return VertexIds.Count >= 3;
    }

    /// <summary>
    /// Checks if vertex IDs are consecutive (0, 1, 2, ..., n-1)
    /// 
    
    /// Range encoding can be applied if IDs are consecutive: v1, v2, v3, v4 → v1-v4
    /// </summary>
    public bool HasConsecutiveIds()
    {
        if (VertexIds.Count == 0)
        {
            return false;
        }

        var sortedIds = VertexIds.OrderBy(id => id).ToList();
        for (int i = 0; i < sortedIds.Count; i++)
        {
            if (sortedIds[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the range encoding of vertex IDs if they are consecutive
    /// 
    
    /// Range encoding: v1, v2, v3, v4 → v1-v4
    /// </summary>
    public string? GetEncodedVertexIds()
    {
        if (HasConsecutiveIds() && VertexIds.Count > 0)
        {
            return DeltaEncoder.EncodeRange(VertexIds);
        }
        return null;
    }

    /// <summary>
    /// Checks if IDs can be encoded as ranges (are consecutive)
    /// </summary>
    public bool CanEncodeAsRange()
    {
        return HasConsecutiveIds();
    }

    /// <summary>
    /// Serializes the temporal polygon to JSON
    /// </summary>
    public string ToJson()
    {
        return Serialization.PolygonJsonSerializer.Serialize(this);
    }

    /// <summary>
    /// Deserializes a temporal polygon from JSON
    /// </summary>
    public static TemporalPolygon FromJson(string json)
    {
        return Serialization.PolygonJsonSerializer.Deserialize(json);
    }
}
