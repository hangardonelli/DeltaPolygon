using DeltaPolygon.Models;

namespace DeltaPolygon.Events;

/// <summary>
/// Event arguments for when a polygon changes
/// </summary>
public class PolygonChangedEventArgs : EventArgs
{
    public Guid PolygonId { get; }
    public PolygonChangeType ChangeType { get; }
    public TemporalPolygon? Polygon { get; }

    public PolygonChangedEventArgs(Guid polygonId, PolygonChangeType changeType, TemporalPolygon? polygon = null)
    {
        PolygonId = polygonId;
        ChangeType = changeType;
        Polygon = polygon;
    }
}

/// <summary>
/// Type of change in a polygon
/// </summary>
public enum PolygonChangeType
{
    Created,
    Updated,
    VertexChanged,
    Deleted
}

/// <summary>
/// Event arguments for when a vertex changes
/// </summary>
public class VertexChangedEventArgs : EventArgs
{
    public Guid PolygonId { get; }
    public int VertexId { get; }
    public DateTime ChangeTime { get; }
    public Point NewPosition { get; }

    public VertexChangedEventArgs(Guid polygonId, int vertexId, DateTime changeTime, Point newPosition)
    {
        PolygonId = polygonId;
        VertexId = vertexId;
        ChangeTime = changeTime;
        NewPosition = newPosition;
    }
}
