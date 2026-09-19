# Map generation porting notes

## Original source

The port is based on the complete `Bird-Hell-Procedural-master/Assets/scripts/MapGenerator.cs`. Serialized component values came from `Assets/Scenes/MainScreen.unity`. `Initialize.cs` was inspected only to confirm generation calls. `MeshGenerator.cs` was not ported.

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
- The mesh call became a renderer-independent `CaveMap`. `BirdHellGame` draws it with a generated 1-by-1 MonoGame texture.

## Intentional differences and confidence limits

- Unity seeded `System.Random` with `seed.GetHashCode()`. Unity/Mono string hashing is runtime-specific. The port uses a stable FNV-1a-style hash over UTF-16 characters and then .NET 10 `System.Random`. Explicit seeds repeat in this standalone target, but matching seed text is not expected to match Unity's cells.
- Unity's `useRandomSeed` replaced its seed with `Time.time.ToString()`. The standalone creates a new observable seed at startup and on `R`; `SPACE` retains the current seed.
- Unity creates a one-cell wall border around the configured map only as `MeshGenerator` input. `CaveMap` remains the configured 128-by-30 logical grid, so the mesh-only 130-by-32 copy is deferred.
- Invalid options are rejected immediately. If no room survives cleanup, the port throws a descriptive `InvalidOperationException`; Unity would fail while indexing `survivingRooms[0]`.
- Exact Unity output equivalence was not verified because of hash/random runtime differences. Generation stages, traversal order, in-place mutation, room selection, and carving rules were ported directly.

## Deferred work

`MeshGenerator.cs`, marching-squares geometry, collider construction, coordinate-to-world conversion, Unity debug lines, and rendering/collision integration beyond rectangular debug tiles remain for later milestones.
