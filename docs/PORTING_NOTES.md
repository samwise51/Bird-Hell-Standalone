# Map generation porting notes

## Original source

The map port is based on the complete `Bird-Hell-Procedural-master/Assets/scripts/MapGenerator.cs`. Serialized component values came from `Assets/Scenes/MainScreen.unity`. `Initialize.cs` was inspected only to confirm generation calls.

## Directly ported behavior

- The grid uses `0` for open space and `1` for walls. Configured edge cells start as walls; each interior cell is a wall when `Random.Next(0, 100)` is below the fill percentage.
- Five smoothing passes scan x then y and update the same map in place. More than four surrounding walls makes a wall, fewer than four makes open space, and exactly four leaves the cell unchanged. All eight neighbors count, with out-of-range positions treated as walls.
- Region flood fill uses cardinal connectivity. Wall regions below 50 cells become open; open regions below 50 cells become walls.
- Rooms retain their tiles, size, connected rooms, and wall-adjacent edge tiles. Edge tiles retain the original implementation's possible duplicates.
- Rooms sort largest first. The largest is the main room and is accessible from it. The original two-stage closest-room algorithm first connects each disconnected room to a nearest candidate, then recursively joins inaccessible and accessible sets by nearest edge tiles. Connections propagate main-room accessibility.
- Passages use the original integer gradient-accumulation line. Every line point carves a clipped, inclusive radius-5 integer circle.

## Replaced Unity dependencies

- `MonoBehaviour` and serialized fields became `CaveGenerator` and `CaveGenerationOptions`.
- `Coord` became `GridCoordinate`; `Mathf` became `System.Math`/integer arithmetic.
- `Time.time` seed text became a timestamp plus GUID from `CaveGenerator.CreateRandomSeed()`.
- The mesh call became a renderer-independent `CaveMap` consumed by the geometry layer described below.

## Intentional differences and confidence limits

- Unity seeded `System.Random` with `seed.GetHashCode()`. Unity/Mono string hashing is runtime-specific. The port uses a stable FNV-1a-style hash over UTF-16 characters and then .NET 10 `System.Random`. Explicit seeds repeat in this standalone target, but matching seed text is not expected to match Unity's cells.
- Unity's `useRandomSeed` replaced its seed with `Time.time.ToString()`. The standalone creates a new observable seed at startup and on `R`; `SPACE` retains the current seed.
- Unity creates a one-cell wall border around the configured map only as `MeshGenerator` input. `CaveMap` remains the configured 128-by-30 logical grid, so the mesh-only 130-by-32 copy is deferred.
- Invalid options are rejected immediately. If no room survives cleanup, the port throws a descriptive `InvalidOperationException`; Unity would fail while indexing `survivingRooms[0]`.
- Exact Unity output equivalence was not verified because of hash/random runtime differences. Generation stages, traversal order, in-place mutation, room selection, and carving rules were ported directly.

## MeshGenerator geometry port

### Original source inspected

Milestone 2 is based on the complete `Bird-Hell-Procedural-master/Assets/scripts/MeshGenerator.cs`, with `MapGenerator.GenerateMap()` inspected again for its exact input construction. No unrelated gameplay source was needed. The Unity source remained read-only.

### Original marching-squares implementation

`MapGenerator` supplies a binary sample grid where `1` is wall/active and `0` is open/inactive. `MeshGenerator.SquareGrid` creates one `ControlNode` per sample and one `Square` per adjacent group of four controls. Each control owns midpoint nodes above and to its right, which makes neighboring squares share the same midpoint objects. A square's configuration is `8 * topLeft + 4 * topRight + 2 * bottomRight + bottomLeft`.

The standalone `CaveGeometryBuilder` retains the original point order and fan triangulation:

| Configuration | Polygon point count | Triangles | Behavior |
| ---: | ---: | ---: | --- |
| 0 | 0 | 0 | Empty |
| 1, 2, 4, 8 | 3 | 1 | One active corner |
| 3, 6, 9, 12 | 4 | 2 | Two edge-adjacent active corners |
| 5, 10 | 6 | 4 | Diagonal active corners are joined as one connected polygon through the square center |
| 7, 11, 13, 14 | 5 | 3 | Three active corners |
| 15 | 4 | 2 | Fully solid square; its corner vertices are pre-marked as checked for outline extraction |

This is intentionally not an ambiguity-resolving textbook variant: configurations 5 and 10 produce connected solid terrain. Polygons are triangle fans rooted at their first listed node. A node receives a vertex index only on first use, so shared `ControlNode` and midpoint instances reuse vertices across neighboring squares. Every emitted triangle is also recorded against each of its three vertex indices for boundary detection.

### Square size, border, and coordinates

Bird Hell calls `GenerateMesh(borderedMap, 1)`, so the standalone default square size is exactly `1` geometry unit. `MapGenerator` first generated its logical 128-by-30 map, then copied it into the center of a 130-by-32 array whose one-cell perimeter was all wall. `CaveGeometryBuilder` now creates that temporary wall sample border while building geometry; it never changes the logical `CaveMap`.

Control sample `(x, y)` is positioned at:

`(-borderedWidth / 2 + x + 0.5, -borderedHeight / 2 + y + 0.5)`

after applying square size. Standalone geometry uses `(X, Y)` where `X` is Unity world `x` and `Y` is Unity world `z`; positive Y is up in geometry space. For the default bordered map, control samples span X `-64.5..64.5` and Y `-15.5..15.5`. Logical tile `(0, 0)` remains centered at `(-63.5, -14.5)`, matching the original `CoordToWorldPoint` X/Z result. MonoGame applies an orthographic projection that maps geometry Y upward and fits these proportions into the window.

### Filled terrain and outlines

The generated triangles represent wall/solid terrain, not traversable open space. An edge is an outline edge when exactly one triangle containing its first vertex also contains its second. Outline walking follows the original triangle insertion order, marks visited vertices, closes each path by repeating its first index, and then removes consecutive collinear points using the original exact direction comparison. Marking configuration-15 corners as checked suppresses the outside edge of the solid wall border; the useful output is therefore the ordered boundary around cave/open-space cavities.

`CaveGeometry` exposes read-only vertices and indexed triangles for filling. Each read-only `CaveOutline` exposes both the ordered geometry vertex indices and copied `(X, Y)` points, including the repeated closing point. These points are the direct standalone counterpart of each Unity `EdgeCollider2D.points` array and are intended to feed later player, enemy, and world collision without rebuilding boundaries from rectangular cells.

### Replaced Unity behavior and intentional differences

- Unity `Node`, `ControlNode`, `Square`, and `SquareGrid` are private builder details rather than public engine-shaped classes.
- Unity `Vector3(x, 0, z)` became renderer-independent `CaveVertex(X, Y)`; there is no Unity Y/elevation component.
- Unity `Mesh.vertices`/`Mesh.triangles` became `CaveGeometry.Vertices`/`Triangles`. UV generation, normal recalculation, `MeshFilter`, and materials are rendering concerns and are not part of geometry.
- `Generate2DColliders` is replaced by exposed `CaveOutline` data. No collision response or physics object is created yet.
- The unused 3D branch (`CreateWallMesh`, five-unit downward extrusion, and `MeshCollider`) is not ported because Bird Hell's required result is 2D cave boundaries.
- Recursive outline following, edge classification, iteration order, shared-node indexing, configuration topology, and exact simplification behavior are preserved. Output collections are read-only snapshots so later rendering cannot mutate collision geometry.
- MonoGame consumes the result using `BasicEffect`, `VertexPositionColor`, indexed triangle drawing, and optional contrasting line-list outlines. It does not inspect `CaveMap` cells for rendering and requires no content-pipeline asset.
