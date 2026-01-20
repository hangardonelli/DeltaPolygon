# DeltaPolygon

A high-performance .NET library for efficient storage and querying of temporal polygons using delta encoding. DeltaPolygon implements a novel model that separates the immutable topological structure from the temporal states of vertices, enabling ultra-efficient storage through relative deltas, vertex clustering, and temporal functions.

## Features

- **Efficient Storage**: Separates immutable topology from temporal vertex states, dramatically reducing storage requirements
- **Delta Encoding**: Uses relative deltas instead of absolute positions when movements are small, significantly reducing data size
- **Vertex Clustering**: Groups vertices with identical changes to minimize repeated rows
- **Temporal Queries**: Efficient O(log H) temporal queries with B-Tree-like indexing
- **Range Encoding**: Compresses consecutive vertex IDs (e.g., `v1-v4` instead of `v1, v2, v3, v4`)
- **LRU Caching**: Built-in LRU cache for frequently accessed reconstructions
- **Precomputation**: Precompute polygon reconstructions at specific times for O(1) access
- **GeoJSON Support**: Convert temporal polygons to GeoJSON format
- **Thread-Safe**: Thread-safe implementation using `ReaderWriterLockSlim` for concurrent reads
- **Multiple Coordinate Systems**: Support for Cartesian and geographic coordinate systems with automatic transformations

## Installation

### NuGet Package

```bash
dotnet add package DeltaPolygon
```

### Manual Installation

Clone the repository and build:

```bash
git clone https://github.com/hangardonelli/DeltaPolygon.git
cd DeltaPolygon
dotnet build
```

## Quick Start

```csharp
using DeltaPolygon.Models;
using DeltaPolygon.Services;

// Create a PolygonDelta service instance
var service = new PolygonDelta();

// Define initial vertices
var vertices = new List<Point>
{
    new Point(0, 0),
    new Point(10, 0),
    new Point(10, 10),
    new Point(0, 10)
};

// Create a polygon at a specific time
var initialTime = DateTime.Now;
var polygon = service.CreatePolygon(vertices, initialTime);

// Reconstruct the polygon at a specific time
var reconstruction = service.GetPolygonAt(polygon.Id, initialTime);

// Update a vertex position (uses delta encoding automatically)
var newTime = initialTime.AddHours(1);
service.UpdateVertex(polygon.Id, vertexId: 0, new Point(2, 2), newTime);

// Get polygon at the new time
var updatedReconstruction = service.GetPolygonAt(polygon.Id, newTime);

// Convert to GeoJSON
var geoJson = service.ToGeoJson(polygon.Id, newTime);
```

## Core Concepts

### Temporal Polygon Structure

A temporal polygon `P` is represented as:

- **Topological Structure**: An ordered sequence of vertex identifiers <img width="188" height="25" alt="image" src="https://github.com/user-attachments/assets/74ea417a-18c9-42a3-94f2-21fbbdb63e05" />

- **Vertex States**: Each vertex has a history of states <img width="220" height="40" alt="image" src="https://github.com/user-attachments/assets/3df8f31d-4c34-498f-9b04-2cbc24c50fd5" />


### Reconstruction Formula

The polygon reconstruction at time `t_target` is:

<img width="335" height="53" alt="image" src="https://github.com/user-attachments/assets/a0bda00e-2de5-40a0-ad8a-c37e592507ee" />



Where (x, y) is computed as:

<img width="250" height="60" alt="image" src="https://github.com/user-attachments/assets/75ae8d1d-ac5c-4bfb-ac3b-b7a213276d58" />


### Delta Encoding

When vertex movements are small (below a configurable threshold), positions are stored as relative deltas instead of absolute coordinates. This reduces storage size significantly while maintaining precision.

### Vertex Clustering

Vertices with identical changes in the same temporal interval are grouped together, minimizing redundant data storage.

## API Reference

### PolygonDelta Service

The main service class for creating and manipulating temporal polygons.

#### Creating Polygons

```csharp
TemporalPolygon CreatePolygon(
    IEnumerable<Point> initialVertices, 
    DateTime initialTime, 
    CoordinateSystem coordinateSystem = CoordinateSystem.Cartesian
)
```

#### Updating Vertices

```csharp
void UpdateVertex(
    Guid polygonId, 
    int vertexId, 
    Point newPosition, 
    DateTime changeTime, 
    bool useDelta = true, 
    double deltaThreshold = 100.0
)
```

#### Querying Polygons

```csharp
// Reconstruct polygon at a specific time
List<Point> GetPolygonAt(Guid polygonId, DateTime time)

// Get vertex position at a specific time
Point? GetVertexPosition(Guid polygonId, int vertexId, DateTime time)

// Get polygon history in a time range
IEnumerable<(DateTime Time, List<Point> Polygon)> GetPolygonHistory(
    Guid polygonId, 
    DateTime startTime, 
    DateTime endTime, 
    TimeSpan? interval = null
)

// Get all polygons that exist in a time range
IEnumerable<TemporalPolygon> GetPolygonsInTimeRange(
    DateTime startTime, 
    DateTime endTime
)
```

#### Precomputation

```csharp
// Mark times for precomputation
void MarkTimeForPrecomputation(Guid polygonId, DateTime time)

// Precompute all marked reconstructions
void PrecomputeMarkedTimes(Guid polygonId)

// Precompute a specific time
void PrecomputePolygonAt(Guid polygonId, DateTime time)
```

#### Batch Updates

```csharp
// Update multiple vertices with the same delta
void UpdateVerticesWithSameDelta(
    Guid polygonId, 
    IEnumerable<int> vertexIds, 
    Point delta, 
    DateTime changeTime
)
```

### TemporalPolygon Model

The core model representing a temporal polygon.

```csharp
public class TemporalPolygon
{
    public Guid Id { get; }
    public ReadOnlyCollection<int> VertexIds { get; }
    public CoordinateSystem CoordinateSystem { get; }
    
    List<Point> ReconstructAt(DateTime time)
    Point? GetVertex(int vertexId)
    bool IsValid()
    string ToJson()
    static TemporalPolygon FromJson(string json)
}
```

## Advanced Usage

### Using Precomputation for Performance

```csharp
// Mark frequently queried times
service.MarkTimeForPrecomputation(polygonId, DateTime.Today);
service.MarkTimeForPrecomputation(polygonId, DateTime.Today.AddDays(1));

// Precompute all marked times
service.PrecomputeMarkedTimes(polygonId);

// Subsequent queries are O(1)
var polygon = service.GetPolygonAt(polygonId, DateTime.Today);
```

### Batch Vertex Updates

```csharp
// Update multiple vertices with the same movement
var vertexIds = new[] { 0, 1, 2, 3 };
var delta = new Point(5, 5);
service.UpdateVerticesWithSameDelta(polygonId, vertexIds, delta, DateTime.Now);
```

### Coordinate System Transformations

```csharp
// Create a polygon in WGS84 (geographic)
var polygon = service.CreatePolygon(
    vertices, 
    DateTime.Now, 
    CoordinateSystem.WGS84
);

// Convert to Cartesian for calculations
var transformer = new CoordinateTransformer();
var cartesianPoints = vertices.Select(p => 
    transformer.Transform(p, CoordinateSystem.WGS84, CoordinateSystem.Cartesian)
);
```

### Event Handling

```csharp
service.PolygonChanged += (sender, args) =>
{
    Console.WriteLine($"Polygon {args.PolygonId} changed: {args.ChangeType}");
};

service.VertexChanged += (sender, args) =>
{
    Console.WriteLine($"Vertex {args.VertexId} moved to {args.NewPosition} at {args.ChangeTime}");
};
```

## Performance Characteristics

- **Storage Efficiency**: Reduces storage by orders of magnitude through delta encoding and vertex clustering
- **Query Performance**: 
  - Precomputed reconstructions: O(1)
  - Cached reconstructions: O(1)
  - Temporal queries: O(log H) where H is the history size
- **Concurrency**: Thread-safe with multiple concurrent readers and synchronized writes
- **Memory**: LRU cache with configurable size (default: 100 entries)

## Architecture

### Key Components

- **PolygonDelta**: Main service class for polygon management
- **TemporalPolygon**: Core model representing a temporal polygon
- **TemporalQueryEngine**: Engine for efficient temporal queries
- **VertexStateManager**: Manages vertex state history and delta encoding
- **PrecomputationService**: Handles precomputation of frequently accessed times
- **GeoJsonConverter**: Converts temporal polygons to GeoJSON format

### Design Principles

1. **Separation of Concerns**: Topology is immutable, states are temporal
2. **Efficiency First**: Delta encoding and clustering minimize storage
3. **Query Optimization**: Multi-level caching (precomputation, LRU cache, on-demand)
4. **Thread Safety**: Safe for concurrent read operations

## Requirements

- .NET 8.0 or later
- No external dependencies (pure .NET Standard 2.1+)

## Testing

Run the test suite:

```bash
dotnet test
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Author

**Lautaro Conde**

## Acknowledgments

This library implements concepts described in research on temporal spatial data structures, focusing on efficient storage and querying of space-time polygons through delta encoding and vertex clustering.

