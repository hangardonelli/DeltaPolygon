using System.Text.Json;
using System.Text.Json.Serialization;
using DeltaPolygon.Models;
using DeltaPolygon.Utilities;
using DeltaPolygon.Coordinates;

namespace DeltaPolygon.Serialization;

/// <summary>
/// JSON serializer for temporal polygons
/// </summary>
public static class PolygonJsonSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Serializes a temporal polygon to JSON
    /// Automatically applies range encoding if vertex IDs are consecutive
    /// </summary>
    public static string Serialize(TemporalPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        // Detect if IDs are consecutive and in natural order to apply range encoding
        var vertexIdsList = polygon.VertexIds.ToList();
        string? vertexIdsEncoded = null;
        List<int>? vertexIds = null;

        // Only encode as range if in exact natural order (0, 1, 2, ..., n-1)
        // This preserves implicit order and optimizes serialization
        if (polygon.HasConsecutiveIds() && IsInNaturalOrder(vertexIdsList))
        {
            // Use range encoding: "0-4" instead of [0, 1, 2, 3, 4]
            // Order is implicit (0, 1, 2, ...), so we don't need to store it
            vertexIdsEncoded = DeltaEncoder.EncodeRange(vertexIdsList);
        }
        else
        {
            // Use normal ID list to preserve topological order
            vertexIds = vertexIdsList;
        }

        var dto = new PolygonDto
        {
            Id = polygon.Id,
            VertexIds = vertexIds,
            VertexIdsEncoded = vertexIdsEncoded,
            CoordinateSystem = polygon.CoordinateSystem,
            Vertices = polygon.Vertices.Select(kvp => new VertexDto
            {
                Id = kvp.Value.Id,
                States = kvp.Value.States.Select(s => new VertexStateDto
                {
                    DeltaX = s.Delta.X,
                    DeltaY = s.Delta.Y,
                    IsAbsolute = s.IsAbsolute,
                    AbsoluteX = s.AbsolutePosition?.X,
                    AbsoluteY = s.AbsolutePosition?.Y,
                    IntervalStart = s.Interval.Start,
                    IntervalEnd = s.Interval.End,
                    
                    GroupedVertexIds = s.GroupedVertexIds?.ToList(),
                    
                    TemporalFunction = s.TemporalFunction != null ? new TemporalFunctionDto
                    {
                        FunctionType = s.TemporalFunction.FunctionType,
                        ReferencePointX = s.TemporalFunction.ReferencePoint.X,
                        ReferencePointY = s.TemporalFunction.ReferencePoint.Y,
                        ReferenceTime = s.TemporalFunction.ReferenceTime,
                        Parameters = s.TemporalFunction.Parameters.ToList()
                    } : null
                }).ToList()
            }).ToList()
        };

        return JsonSerializer.Serialize(dto, JsonOptions);
    }

    /// <summary>
    /// Deserializes a temporal polygon from JSON
    /// Automatically decodes encoded ranges if present
    /// </summary>
    public static TemporalPolygon Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var dto = JsonSerializer.Deserialize<PolygonDto>(json, JsonOptions) ?? throw new ArgumentException("JSON inválido o vacío", nameof(json));
        var vertices = new Dictionary<int, Vertex>();
        var vertexIds = new List<int>();

        foreach (var vertexDto in dto.Vertices)
        {
            var vertex = new Vertex(vertexDto.Id);
            vertexIds.Add(vertexDto.Id);

            foreach (var stateDto in vertexDto.States)
            {
                var interval = new TimeInterval(stateDto.IntervalStart, stateDto.IntervalEnd);
                
                // Convertir GroupedVertexIds si existe
                IReadOnlyList<int>? groupedVertexIds = stateDto.GroupedVertexIds?.AsReadOnly();

                VertexState state;
                
                
                if (stateDto.TemporalFunction != null && 
                    stateDto.TemporalFunction.FunctionType != TemporalFunctionType.Custom)
                {
                    var referencePoint = new Point(
                        stateDto.TemporalFunction.ReferencePointX, 
                        stateDto.TemporalFunction.ReferencePointY);
                    
                    var temporalFunction = TemporalFunction.FromParameters(
                        stateDto.TemporalFunction.FunctionType,
                        referencePoint,
                        stateDto.TemporalFunction.ReferenceTime,
                        stateDto.TemporalFunction.Parameters?.ToArray() ?? Array.Empty<double>()
                    );
                    
                    if (temporalFunction != null)
                    {
                        state = new VertexState(temporalFunction, interval, groupedVertexIds);
                    }
                    else
                    {
                        // Fallback to absolute state if function could not be reconstructed
                        state = new VertexState(
                            new Point(stateDto.AbsoluteX ?? 0, stateDto.AbsoluteY ?? 0),
                            interval,
                            isAbsolute: true,
                            groupedVertexIds: groupedVertexIds
                        );
                    }
                }
                else if (stateDto.IsAbsolute && stateDto.AbsoluteX.HasValue && stateDto.AbsoluteY.HasValue)
                {
                    state = new VertexState(
                        new Point(stateDto.AbsoluteX.Value, stateDto.AbsoluteY.Value),
                        interval,
                        isAbsolute: true,
                        groupedVertexIds: groupedVertexIds
                    );
                }
                else
                {
                    state = new VertexState(
                        new Point(stateDto.DeltaX, stateDto.DeltaY), 
                        interval,
                        groupedVertexIds: groupedVertexIds
                    );
                }

                vertex.AddState(state);
            }

            vertices[vertexDto.Id] = vertex;
        }

        // Decodificar vertexIds: puede venir como lista o como rango codificado
        List<int> orderedVertexIds;
        if (!string.IsNullOrEmpty(dto.VertexIdsEncoded))
        {
            // Decodificar rango: "0-4" -> [0, 1, 2, 3, 4]
            orderedVertexIds = DeltaEncoder.DecodeRange(dto.VertexIdsEncoded);
        }
        else if (dto.VertexIds != null && dto.VertexIds.Count > 0)
        {
            // Usar lista normal
            orderedVertexIds = dto.VertexIds.ToList();
        }
        else
        {
            // Fallback: use vertex IDs in order
            orderedVertexIds = vertexIds;
        }

        // Obtener el sistema de coordenadas del DTO (por defecto Cartesian si no está especificado)
        var coordinateSystem = dto.CoordinateSystem ?? CoordinateSystem.Cartesian;

        return new TemporalPolygon(dto.Id, orderedVertexIds, vertices, coordinateSystem);
    }

    /// <summary>
    /// Verifica si los IDs están en orden natural exacto (0, 1, 2, ..., n-1)
    /// </summary>
    private static bool IsInNaturalOrder(List<int> ids)
    {
        if (ids.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] != i)
            {
                return false;
            }
        }

        return true;
    }

    // DTOs para serialización JSON
    private class PolygonDto
    {
        public Guid Id { get; set; }
        
        /// <summary>
        /// List of vertex IDs (used when they are not consecutive)
        /// </summary>
        public List<int>? VertexIds { get; set; }
        
        /// <summary>
        /// Vertex IDs encoded as range (e.g., "0-4" for [0,1,2,3,4])
        /// Used automatically when IDs are consecutive to optimize serialization
        /// </summary>
        public string? VertexIdsEncoded { get; set; }
        
        /// <summary>
        /// Polygon coordinate system (default Cartesian)
        /// </summary>
        public CoordinateSystem? CoordinateSystem { get; set; }
        
        public List<VertexDto> Vertices { get; set; } = new();
    }

    private class VertexDto
    {
        public int Id { get; set; }
        public List<VertexStateDto> States { get; set; } = new();
    }

    private class VertexStateDto
    {
        public double DeltaX { get; set; }
        public double DeltaY { get; set; }
        public bool IsAbsolute { get; set; }
        public double? AbsoluteX { get; set; }
        public double? AbsoluteY { get; set; }
        public DateTime IntervalStart { get; set; }
        public DateTime? IntervalEnd { get; set; }
        
        /// <summary>
        /// Additional vertex IDs sharing this state
        
        /// </summary>
        public List<int>? GroupedVertexIds { get; set; }
        
        /// <summary>
        /// Función temporal para calcular posiciones
        
        /// </summary>
        public TemporalFunctionDto? TemporalFunction { get; set; }
    }

    /// <summary>
    /// DTO for serializing temporal functions
    
    /// "The function is stored instead of each individual state"
    /// </summary>
    private class TemporalFunctionDto
    {
        /// <summary>
        /// Function type (Linear, Circular)
        /// </summary>
        public TemporalFunctionType FunctionType { get; set; }
        
        /// <summary>
        /// X coordinate of reference point
        /// </summary>
        public double ReferencePointX { get; set; }
        
        /// <summary>
        /// Y coordinate of reference point
        /// </summary>
        public double ReferencePointY { get; set; }
        
        /// <summary>
        /// Reference time (t0)
        /// </summary>
        public DateTime ReferenceTime { get; set; }
        
        /// <summary>
        /// Function parameters:
        /// - Linear: [velocityX, velocityY]
        /// - Circular: [radius, angularVelocity, phase]
        /// </summary>
        public List<double>? Parameters { get; set; }
    }
}
