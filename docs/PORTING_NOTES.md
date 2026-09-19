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
- Unity's `useRandomSeed` replaced its seed with `Time.time.ToString()`. The standalone creates a new observable seed at startup and on `R`; `F5` regenerates the current seed and respawns, while `SPACE` is reserved for the original jump input.
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

## Player, movement, and terrain collision

### Original implementation traced

The following original files were read in full: `Assets/scripts/Player.cs`, `Assets/scripts/PlayerMovement.cs`, `Assets/NewCharacterController2D.cs`, `Assets/scripts/CharacterController2D.cs`, `Assets/scripts/PlayerMovementmobile.cs`, `Assets/scripts/preventSlipping.cs`, and `Assets/scripts/Initialize.cs`. The corresponding `.meta` GUIDs were traced through `Assets/Scenes/MainScreen.unity`, `Assets/prefab/player.prefab`, `Assets/prefab/player mobile.prefab`, and `Assets/prefab/Initialize.prefab`. `ProjectSettings/InputManager.asset`, `Physics2DSettings.asset`, `TimeManager.asset`, and `EditorBuildSettings.asset` were also inspected. The Unity tree remained read-only.

`MainScreen.unity` is the sole enabled build scene and establishes the current procedural-game controller. Its active `player` object directly contains `NewCharacterController2D` (GUID `cb234e8f44f17de44b14fadb30c43e41`), a `CapsuleCollider2D`, `Rigidbody2D`, `Player`, weapon and trigger components. `Initialize.ctrl` points to that exact controller and `Initialize.plyr` to that exact player transform. The desktop `PlayerMovement` component is enabled on the separate `Initialize` object; its current source only drives animation/reload/fall recovery and does not call either movement controller. This is why `NewCharacterController2D`, rather than the older `CharacterController2D`, is treated as authoritative.

The reusable `player.prefab` represents an older setup: it has `CharacterController2D` with jump force 650, smoothing 0.05, air control enabled, and an enabled serialized `PlayerMovement` component whose old fields no longer match the current source. It has no `NewCharacterController2D`. The mobile prefab similarly uses `CharacterController2D`/`PlayerMovementmobile` with jump force 700 and run speed 40. These prefab paths and disabled level scenes are legacy or mobile evidence, not the active procedural scene configuration.

### Active serialized values and collision body

`MainScreen.unity` serializes movement speed `8`, ground-check radius `0.18`, jump impulse `18`, slope-check distance `2`, maximum slope angle `45`, fall multiplier `4.5`, low-jump multiplier `3`, facing direction `1`, and maximum grounded angle `45`. Unity's fixed timestep is `0.02` seconds and maximum allowed timestep is `0.1` seconds.

The active physical shape is a vertical `CapsuleCollider2D`, local size `(0.073097564, 0.29390925)`, local offset `(0.0028309114, -0.032838672)`, on a transform scaled uniformly by `4`. Its effective world size is therefore approximately `(0.29239026, 1.175637)`, with world offset `(0.01132365, -0.13135469)`. A circle collider is disabled. Two enabled box colliders are triggers and are unrelated to terrain response.

The active rigid body is dynamic and simulated, mass `1`, linear drag `1`, angular drag `1`, serialized gravity scale `3`, continuous collision detection, no interpolation, full kinematic contacts enabled, and Z rotation frozen. `NewCharacterController2D.Start()` immediately sets gravity scale to zero and applies custom gravity, so the serialized gravity scale does not govern gameplay. Project gravity is `(0, -9.81)`. Unity's default 2D contact offset is `0.01`.

### Original movement and jumping behavior

`Input.GetAxisRaw("Horizontal")` selects `-1`, `0`, or `1`; the desktop Input Manager maps Left/Right and alternate `A`/`D`. There is no acceleration or deceleration curve in the active controller. Every fixed update assigns horizontal velocity directly to `movementSpeed * input`. In air it retains vertical velocity and still assigns full horizontal speed, so air control is unrestricted. On walkable slopes it assigns the full speed along `slopeNormalPerp`. Facing flips immediately when input direction changes.

Jump maps to `SPACE`. On each accepted button-down the controller clears the entire rigid-body velocity, adds an upward impulse of `18`, marks jumping, and increments `j`. The implemented condition is `j < 2`; grounding resets `j` to zero. Despite unused/commented `canJump` and `canDoubleJump` code, the active path therefore explicitly permits two jumps total: one ground jump and one air jump. While rising, releasing Jump increases effective downward acceleration to the `3` multiplier; falling uses `4.5`. Holding Jump while rising uses base gravity.

### Original grounding, slopes, friction, and gravity uncertainty

Grounding uses `Physics2D.OverlapCircle` at child `GroundCheck`, scene-local position `(0.0034, -0.1337)`, radius `0.18`, and layer mask bits `5943`. A horizontal ray is cast both ways from `transform.position - (0, capsuleHeight / 2)` for distance `2`; a downward ray from the same point finds the floor normal and slope angle. A side hit at exactly 90 degrees forces ungrounded state. Slopes at or below 45 degrees are walkable; steeper slopes are rejected. On a walkable slope with no horizontal input, the controller swaps to the `friction` material (friction `100`, bounce `0`); otherwise it uses `slippery` (friction `0.01`, bounce `0`).

The controller calls `ObeyGravity`, ground check, slope checks, and movement in `FixedUpdate`, then applies the enhanced jump gravity. Airborne custom gravity is project gravity. Its intended grounded gravity is along the negative contact normal to prevent sliding. However, the calls that populate `groundNormal` through `CheckGrounded` are commented out, so the checked-in script does not clearly establish a nonzero grounded contact normal. The standalone intentionally implements stable support and no idle slope slipping rather than reproducing that apparent bug. Exact Unity force integration order relative to the post-`AddForce` velocity checks is also engine-specific.

### Original and standalone spawning

`Initialize.Awake()` generates the map. `Start()` then calls `TranslatePlayer`, and reload calls generation followed by `TranslatePlayer`. The original obtains generated `EdgeCollider2D[]`, selects element zero, and sets the player transform to `(firstCollider.bounds.center.x - 5, firstCollider.bounds.center.y - 5)`. It does not verify clearance, choose a floor, reset velocity, or prove which generated outline becomes collider zero. This can embed the player and cannot safely be copied as-is.

The standalone preserves the original `first/primary outline bounds center X - 5` preference, then deterministically searches nearby floor intersections from the ordered `CaveOutline` segments. Candidates are rejected if the effective capsule penetrates any boundary or sampled capsule points lie in filled `CaveGeometry` triangles. The selected spawn is therefore geometry-derived, deterministic for a same-seed cave, and clear of solid terrain. Both `R` and `F5` reset position, velocity, jump state, simulation accumulator, and camera.

### Standalone architecture and intentional differences

- `Entities/Player.cs` owns renderer-independent position, velocity, grounded/jump state, ground normal, and the effective Unity capsule dimensions.
- `Systems/PlayerMovementSystem.cs` owns input-independent 50 Hz movement, custom gravity, jump limiting, variable jump height, full air control, and slope-tangent movement.
- `Systems/TerrainCollisionSystem.cs` converts only ordered `CaveOutline` points to line segments. It resolves a vertical capsule against floors, walls, ceilings, and diagonal segments with displacement substeps, penetration projection, velocity clipping, a `0.0025` skin, and a `0.035` support probe. It never consults rectangular `CaveMap` cells.
- `Systems/PlayerSpawnSystem.cs` selects deterministic safe positions using outlines and filled geometry triangles.
- `Core/BirdHellGame.cs` adapts MonoGame keyboard state, accumulates fixed `0.02` simulation steps independently of rendering, renders a temporary capsule, and follows/clamps the camera horizontally.

The collision solver is a focused standalone replacement for Unity Rigidbody2D/Physics2D; no third-party physics engine is used. It preserves direct speed assignment, two jumps, 45-degree walkable slopes, slope following, variable gravity, effective collider dimensions, and no-bounce/no-idle-slip results. It does not reproduce Box2D drag, material-combine details, contact solver iteration order, or the apparent uninitialized-ground-normal bug. An airborne player at exactly zero vertical speed is treated as falling so the gravity multiplier does not depend on fixed-step size. The safe spawn is deliberately stricter than Unity's unchecked transform assignment. The camera is intentionally limited to horizontal following with level-bound clamping.

`SPACE` is now exclusively jump. Same-seed regeneration moved to `F5`; `R` creates a new seed, `O` toggles terrain outlines, `C` toggles capsule/ground-normal debug rendering, and `ESC` exits.

### Deferred coupling

`Player.cs` health, damage flash, audio, death effect, activation state, and health reset are deferred. Animator speed/jump/crouch/attack flags, sprite flipping, weapon/fire point/bullets, crouching, pause/reload UI, enemy spawning/targets, A* rescans, mobile joystick/buttons, out-of-bounds teleport, scene transitions, and sophisticated camera behavior are also deferred. None is required for Milestone 3 movement or terrain collision.
