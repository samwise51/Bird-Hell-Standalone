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

The standalone preserves the original `first/primary outline bounds center X - 5` preference, then deterministically searches nearby floor intersections from the ordered `CaveOutline` segments. Candidates are rejected if the effective capsule penetrates any boundary or sampled capsule points lie in filled `CaveGeometry` triangles. The selected spawn is therefore geometry-derived, deterministic for a same-seed cave, and clear of solid terrain. Both `R` and `F5` reset position, velocity, jump state, and the simulation accumulator; Milestone 7 intentionally preserves camera position across replacement.

### Standalone architecture and intentional differences

- `Entities/Player.cs` owns renderer-independent position, velocity, grounded/jump state, ground normal, and the effective Unity capsule dimensions.
- `Systems/PlayerMovementSystem.cs` owns input-independent 50 Hz movement, custom gravity, jump limiting, variable jump height, full air control, and slope-tangent movement.
- `Systems/TerrainCollisionSystem.cs` converts only ordered `CaveOutline` points to line segments. It resolves a vertical capsule against floors, walls, ceilings, and diagonal segments with displacement substeps, penetration projection, velocity clipping, a `0.0025` skin, and a `0.035` support probe. It never consults rectangular `CaveMap` cells.
- `Systems/PlayerSpawnSystem.cs` selects deterministic safe positions using outlines and filled geometry triangles.
- `Core/BirdHellGame.cs` adapts MonoGame keyboard state, accumulates fixed `0.02` simulation steps independently of rendering, renders a temporary capsule, and advances a renderer-independent 2D camera at the same cadence.

The collision solver is a focused standalone replacement for Unity Rigidbody2D/Physics2D; no third-party physics engine is used. It preserves direct speed assignment, two jumps, 45-degree walkable slopes, slope following, variable gravity, effective collider dimensions, and no-bounce/no-idle-slip results. It does not reproduce Box2D drag, material-combine details, contact solver iteration order, or the apparent uninitialized-ground-normal bug. An airborne player at exactly zero vertical speed is treated as falling so the gravity multiplier does not depend on fixed-step size. The safe spawn is deliberately stricter than Unity's unchecked transform assignment. The camera now follows both axes without cave-bound clamping, matching the active MainScreen audit.

`SPACE` is now exclusively jump. Same-seed regeneration moved to `F5`; `R` creates a new seed, `O` toggles terrain outlines, `C` toggles capsule/ground-normal debug rendering, and `ESC` exits.

### Deferred coupling

Player health, damage feedback, death state, health reset, enemy spawning/targets, deterministic A* rebuilding, and the active camera behavior are implemented. Audio, death effects, animator flags, crouching, pause/reload UI, mobile joystick/buttons, out-of-bounds teleport, and scene transitions remain deferred.

## Combat, weapon, and projectiles

### Original source and active wiring

The combat trace covered `Assets/scripts/Weapon.cs`, `Bullet.cs`, `Player.cs`, `PlayerMovement.cs`, `Weaponmobile.cs`, `PlayerMovementmobile.cs`, `Enemy.cs`, `NewCharacterController2D.cs`, `MeshGenerator.cs`, and `DestroyClone.cs`. Script GUIDs were followed through `Assets/Scenes/MainScreen.unity`, `Assets/prefab/Bullet.prefab`, `player.prefab`, `player mobile.prefab`, `Enemy.prefab`, and `ImpactEffect.prefab`. `ProjectSettings/InputManager.asset`, `Physics2DSettings.asset`, `TagManager.asset`, and `EditorBuildSettings.asset` supplied input, layer, and active-scene evidence. The Unity project remained read-only.

`EditorBuildSettings.asset` enables only `MainScreen.unity`. Its active, scene-embedded `player` object has an enabled `Weapon` component (script GUID `af8d2921039bafa4d8f3f4d9bd6ec992`) whose `firePoint` references the player's `FirePoint` child and whose `bulletPrefab` references `Assets/prefab/Bullet.prefab` (GUID `c78e053e14e4b354ba7a747474680066`). This is the authoritative desktop implementation. The reusable desktop player prefab has the same configured `Weapon`; the separate `Weaponmobile`/button path is not active in MainScreen and its scene-relevant references are unset.

There is no distinct weapon GameObject or selectable weapon prefab. `Weapon` is a component on the player. There is no weapon switching, recoil, knockback, spread, randomization, magazine, reserve ammunition, reload, reload duration, or time-based fire-rate field anywhere in this path.

### Input, direction, and serialized values

`Weapon.Update()` calls `Shoot()` only on `Input.GetButtonDown("Fire1")`, so one projectile is created per button-down edge and holding the button does not repeat. The desktop `Fire1` mapping is Left Ctrl with left mouse as its alternate; a second same-named Input Manager entry maps joystick button 0. The standalone ports these as Left Ctrl, left mouse, and controller A. No reload control exists.

There is no mouse-position read, screen-to-world conversion, angle calculation, or aim script in the original combat path. `Weapon.Shoot()` instantiates at `firePoint.position` and `firePoint.rotation`; `Bullet.Start()` assigns `rb.velocity = transform.right * speed`. MainScreen serializes the fire point at local `(0.086, 0, 0)` under the uniformly scale-4 player, giving an effective `0.344` world-unit muzzle offset. `NewCharacterController2D.Flip()` rotates the player 180 degrees around Y, so inherited `transform.right` and the muzzle offset reverse when facing changes. Aim is therefore horizontal and facing-based. Mouse/world camera conversion is not applicable, and the horizontal camera cannot skew aim.

The active serialized projectile values are:

- speed `20` world units/second;
- damage `25`;
- no lifetime or delayed destruction;
- dynamic `Rigidbody2D`, simulated, mass `1`, linear drag `0`, angular drag `0.05`, gravity scale `0`, continuous collision detection, no interpolation, and frozen Z rotation;
- trigger `CircleCollider2D`, local offset `(0.34, 0.28)`, radius `0.34`, enabled;
- prefab transform scale `(1, 1, 1)`;
- GameObject layer `0` (`Default`).

The collider's positive local X offset follows the facing flip while its Y offset remains upward. The standalone preserves both offsets and radius. With zero gravity and direct initial velocity, a projectile travels at constant speed. No script or prefab gives the bullet a finite lifetime, so a miss can persist indefinitely in Unity.

### Original impact and damage behavior

`Bullet.OnTriggerEnter2D` asks the contacted collider's GameObject for `Enemy`. If found, it calls `Enemy.TakeDamage(25)`. It then creates `ImpactEffect` and destroys the bullet regardless of collider type. `Enemy.TakeDamage` subtracts from serialized health `100`; at zero it plays death coupling, updates the kill tracker, creates a death effect, and destroys the enemy. The impact prefab is animation-only plus `DestroyClone`, which schedules all objects tagged `clone` for destruction after `0.4` seconds.

The bullet uses Default layer 0; generated cave `EdgeCollider2D` components are placed on the layer-12 Map Generator object, enemies are layer 12, and the player is layer 11. `Physics2DSettings.asset` has an all-enabled layer collision matrix, so none of these pairs is filtered. Consequently an ordinary terrain trigger contact destroys the bullet. A bullet can also trigger against a player collider and be destroyed, although only an `Enemy` component receives damage; there is no explicit owner/self-ignore or player damage path. The forward muzzle/collider offsets normally place the shooter's own bullet ahead of the player.

Callback ordering for exactly simultaneous trigger contacts is Unity/Box2D-specific. The checked-in source also does not state how long a projectile that never contacts anything is intended to remain alive; observed code has no cleanup. Duplicate same-named `Fire1` Input Manager entries make controller aggregation an engine-level detail, but the keyboard/mouse mapping is unambiguous.

### Standalone architecture

- `Entities/Projectile.cs` stores renderer-independent transform position, normalized direction, constant velocity inputs, damage, exact collider geometry, age, and optional lifetime data.
- `Entities/WeaponState.cs` records deterministic shots fired. It deliberately has no ammo or reload state because the original has neither.
- `Systems/WeaponSystem.cs` owns the serialized muzzle/projectile configuration and creates one projectile for each supplied Fire1 edge. It derives horizontal aim solely from `Player.FacingDirection`.
- `Systems/ProjectileSystem.cs` owns live projectiles, fixed-step motion, terrain impact removal, lifetime handling when explicitly configured, and nearest-hit damage dispatch through the minimal `IDamageable`/`ProjectileTarget` abstraction. Milestone 5 supplies living enemy circle targets through that same abstraction.
- `Systems/TerrainCollisionSystem.TrySweepCircle` continuously sweeps the projectile circle against the exact line segments derived from `CaveGeometry.Outlines`. It checks the segment strip and both endpoint caps, so a projectile cannot tunnel through ordinary outline terrain at expected or test-elevated speeds. It never consults `CaveMap` cells.
- `Core/BirdHellGame.cs` queues Left Ctrl, left-mouse, and controller-A press edges into the existing 50 Hz simulation, resets combat on cave regeneration, and draws temporary yellow projectile diamonds. Its horizontal muzzle/facing aim line is shown only with the existing `C` player collision/facing debug view. Rendering does not own or advance simulation state.

Equivalent input and cave geometry produce deterministic projectile state. Facing is now explicit player simulation state and resets to the MainScreen serialized value `+1` on respawn. There is no fire cooldown to count down and ammunition is unlimited, matching the source. Production projectile lifetime remains `null`; optional lifetime support exists for later serialized variants and is deterministically checked but is not enabled as a Bird Hell mechanic.

### Intentional differences and deferred combat work

Terrain and abstract damage-target contacts are implemented now. Exact Unity trigger interaction with unrelated dynamic colliders, including consuming a projectile on the player, is deferred until those collider-owning entities exist in the standalone. If a terrain and damage target are hit in one step, the standalone resolves the earliest swept hit (target wins an exact tie) rather than relying on unspecified Unity callback order. Continuous collision is an analytic circle sweep rather than Unity Rigidbody2D/Box2D.

The impact animation and its broad tag-based cleanup, bullet/impact artwork, bullet animator, player attack animation flag, all audio, death effects, mobile fire button, and final combat UI remain deferred. Enemy damage flash, death tracking, AI, and spawning are implemented by Milestone 5. There is no ammo or reload UI to defer because the original active weapon has no ammo/reload model. Mouse aiming, fire-rate timers, recoil, spread, and weapon switching were not added because they are absent from the traced implementation.

## Flying enemies, navigation, health, and death

### Active original values

`MainScreen.unity` creates 15 instances of the flying enemy. Each has health `100`; the active bullet deals `25`, so four hits kill a default enemy. `Initialize.SpawnEnemies()` independently assigns `AIPath.maxSpeed = Random.Range(2, 10)`, producing integer speeds 2 through 9. The active `AIPath` values are maximum acceleration `10`, slowdown distance `0.6`, end-reached distance `0.2`, repath interval `0.5`, pick-next-waypoint distance `2`, no gravity, and no rotation. `birdGFX` faces by horizontal desired velocity.

The effective enemy circle collider has radius approximately `0.45807048` and offset approximately `(-0.15384216, -0.07692099)`. Active scene serialization overrides the source default and sets player contact damage to `25`. Every enemy increments its own timer on every `0.02` fixed update, including while not touching. While overlapping, damage occurs only when that timer is strictly greater than one second and then only that enemy's timer resets. This yields a nominal 1.02-second repeat cadence. There is no knockback, shared invulnerability, enemy attack animation, enemy projectile, or enemy gravity. Death removes the enemy; the final death only logs `All Birds Dead`.

### Original A* configuration

The Unity project uses A* Pathfinding Project 4.2.15 and a `GridGraph` centered at `(0,0)`, sized `128 × 72`, with node size `1`. It uses eight-way neighbors, disables diagonal corner cutting, checks obstacles with radius `0.5`, recalculates every `0.5` seconds, rejects partial paths, keeps an old path after a failed replan, and permits nearest-node resolution up to distance `100`. Obstacles are generated `EdgeCollider2D` outlines, not logical `CaveMap` wall cells.

### Standalone architecture

- `Entities/Enemy.cs` owns transform/velocity, exact effective collider geometry, 100 health, integer maximum speed, per-enemy contact/repath state, facing, damage feedback, path state, and `IDamageable` behavior.
- `World/NavigationGrid.cs` builds a renderer-independent `128 × 72`, one-unit, origin-centered graph. A node is blocked when its center lies within `0.5` of any `CaveOutline` segment. It exposes deterministic eight-way neighbors and rejects diagonals when either adjacent cardinal node is blocked.
- `Systems/AStarPathfinder.cs` resolves nearest walkable start/end cells within 100 units, uses deterministic `(f, heuristic, index)` tie ordering, returns ordered world-space points, and returns no partial path. A failed enemy replan leaves the prior valid path untouched.
- `Systems/EnemyNavigationSystem.cs` replans at 0.5 seconds in the 50 Hz simulation, looks ahead across waypoints within 2 units, accelerates by at most 10 units/sec², clamps randomized speed, slows over the final 0.6 units, and stops within 0.2. Circle sweeps reuse `TerrainCollisionSystem` outline segments. A two-pass deterministic pairwise circle separation is intentionally small and is not flocking or RVO.
- `Systems/EnemyCombatSystem.cs` continuously advances independent contact timers and tests the enemy circle against the player's effective capsule. A strictly-greater-than-one-second overlapping timer deals 25 with no knockback or global immunity.
- `Systems/EnemySpawnSystem.cs` and `EnemyPopulation.cs` own deterministic reset, 15-enemy creation, living-target projection, living count, and `AllEnemiesDead`.
- `Player` now implements `IDamageable`, starts at 100/100 health, becomes inactive for movement and firing at zero, and resets to alive with full health whenever the cave regenerates.

Initialization is explicit: generate `CaveMap`; build `CaveGeometry`/`CaveOutline`; prepare terrain collision; reset/spawn the player; build navigation; clear old enemies; spawn 15 enemies; begin fixed-step gameplay. `R` and `F5` both rebuild all gameplay state, with `F5` reproducing the same enemy speeds and initial positions for the same seed.

### Spawn stabilization and intentional differences

Unity places all 15 enemies at the exact unchecked center of the first generated edge-collider bounds. The standalone derives that same first-outline bounds-center reference, then applies only these stabilizations:

1. resolve the reference to the nearest walkable one-unit navigation-cell center, ensuring the original `0.5` obstacle scan clearance;
2. add deterministic golden-angle offsets of `0.0125 × sqrt(enemyIndex)` units so no pair begins at mathematically identical coordinates, falling back to the safe center if an offset intersects an outline;
3. run the small deterministic circle-separation pass during simulation so the still-tightly-clustered circles do not remain catastrophically overlapped.

This remains one localized original-style cluster. It does not distribute enemies among rooms, add local avoidance, or redesign spawning. Enemy speed randomization uses a stable seed hash so same-seed resets are reproducible, unlike Unity's process-global random sequence. Startup/reload deliberately avoids the original Awake/scan race, stale player target, deferred-destruction overlap, and player-as-static-obstacle artifacts. Analytic outline collision replaces Rigidbody2D separation; tie/callback details therefore do not depend on Box2D ordering.

Temporary orange primitives distinguish enemies from the cyan player. White flashes show damage; a dead player is gray. The title reports health, living enemies, `PLAYER DEAD`, and `ALL BIRDS DEAD`. `N` shows a lightweight sample of blocked graph cells, the first living enemy's path, and its steering line without crowding normal play.

### Deferred enemy functionality

Final bird/player artwork, animator coupling, audio, hit/death particle prefabs, UI screens, health bars, cleared-screen/next-level/win transitions, mobile controls, enemy projectiles, attacks beyond contact, and sophisticated RVO/flocking remain deferred. `AllEnemiesDead` is state/debug feedback only.

## Procedural MainScreen lifecycle and atomic reset

### Audited active loop

The checked-in Unity build has one enabled gameplay scene, `MainScreen`. Its active loop is: generate a random procedural cave, position the existing player, spawn 15 enemies, and play. Enemy death removes that enemy; the final death only logs `All Birds Dead`. Player health starts at 100, enemy contact deals 25, and health at or below zero deactivates the player indefinitely. There is no automatic restart after death and no automatic regeneration after clearing the birds.

The active MainScreen has no gameplay Canvas/UI, level number, difficulty increase, scene transition, or reachable win path. `Level1` through `Level7`, `Cleared`, `Win`, `LoadCurrentScene`, `MySceneManager`, and scene-index progression are legacy, disabled, disconnected, or broken content and are explicitly excluded from this port.

Unity maps its `Start` input to `Escape`; `Initialize.Update()` uses that edge to regenerate in place. The standalone retains its established, unambiguous `ESC = exit` behavior. `R` is the production equivalent of Unity's Start/Escape regeneration. `F5` is an intentional standalone/debug extension for exact-current-seed regeneration.

### Derived run states

`Core/RunState.cs` names `Playing`, `PlayerDead`, and `AllBirdsDead`. `Core/MainScreenRun.cs` does not store a second mutable lifecycle flag: it derives state from authoritative player life/health and living-enemy population.

- `Playing` means the player is alive and one or more birds remain.
- `PlayerDead` means player health is zero/inactive. Movement and firing reject input, enemies may remain, and no timer, respawn, scene load, or cave regeneration occurs.
- `AllBirdsDead` means the player is alive and no birds remain. The current cave and player continue normally, and there is no replacement spawn, regeneration, difficulty change, level advance, or scene load.

The fixed simulation order is player movement, projectile creation/motion and enemy damage, enemy navigation, then enemy contact damage. That order does not normally produce simultaneous player death and final-enemy death because a bird killed by the projectile phase cannot deal contact damage later in the step. If tests or future external integration force both facts true, `PlayerDead` deterministically takes precedence; `AllBirdsDead` specifically requires a living player.

### One atomic run pipeline

`MainScreenRun.StartRun(seed)` is the sole authoritative pipeline used by startup, `R`, and `F5`. It first constructs a complete unpublished replacement run: cave map, cave geometry/outlines, terrain collision, safely spawned/contact-refreshed player, navigation grid built from the new outlines, exactly 15 deterministic fresh enemies, enemy navigation/pathfinder, empty projectile system, and fresh weapon state. Only after every construction step succeeds are all run references published. A failed construction therefore cannot expose a half-old/half-new simulation.

The replacement clears or refreshes player health/life, position, velocity, right-facing direction, grounded/contact data, jump counters/state, and damage flash; weapon shot/transient state; all projectiles; every old enemy and its health, life, velocity, path/index, repath/contact timers, facing, steering, and flash state; the old terrain/navigation/pathfinder; pending jump/fire edges; fixed-step accumulator; and dynamic first-living-enemy debug selection. The host suppresses gameplay edges on the reset frame, so pressing reset with fire or jump cannot leak that edge into the new run. Camera position and debug preferences are presentation state and deliberately survive the atomic gameplay replacement; the camera receives only the current player's numeric position on later fixed steps, so it cannot retain a stale entity reference.

The `O`, `C`, and `N` visualization toggles are user presentation preferences and intentionally survive a reset. The standalone also preserves the selected seed only for `F5`; `R` always requests a different seed. Same-seed setup reproduces cave geometry, enemy initial positions/speeds, and other intentionally deterministic setup.

### Unity reset defects intentionally fixed

The original restart path mixes immediate creation with Unity's deferred object destruction and scene searches/scans. Depending on callback timing, old generated colliders/enemies can coexist briefly with replacements, navigation can scan stale geometry, and target/entity references can survive into the regenerated run. It also repositions the player without comprehensively resetting rigid-body, jump, combat, and feedback state. The standalone intentionally fixes those races by staging and swapping a complete run, rebuilding navigation only after new geometry exists, and never carrying entity/path/projectile references across reset.

These fixes do not add gameplay. Player death and final-bird death remain persistent terminal conditions that require manual reset. No level progression, score, lives, checkpoints, menu, HUD, death/victory screen, save system, or scene-management path was introduced.

## 2D camera and presentation foundation

### Active Unity camera behavior

The active `MainScreen` camera is orthographic with size `5`, giving a fixed 10-world-unit vertical span. Its `SmoothCam` target offset is `(0,1)`. On every Unity `FixedUpdate`, it assigns `camera = Lerp(camera, player.position + offset, 0.15)`. The factor is therefore exactly `0.15` per fixed simulation step rather than a delta-time-normalized smoothing rate. It follows both X and Y with no dead zone, cave-bound clamp, velocity look-ahead, or shake. Its initial XY position is `(0,0)`, and regeneration does not reposition it; after the player teleports, it travels smoothly to the replacement target. Player death and final-bird death do not alter camera state. The background is a camera child and has no parallax.

### Standalone camera and projection

`Presentation/Camera2D.cs` owns only logical `System.Numerics.Vector2` position and the audited constants. `BirdHellGame` passes the current player's value position after each 0.02-second gameplay step. `Camera2D` stores no `Player`, run, geometry, renderer, or depth reference. A fresh host leaves it at `(0,0)` until fixed simulation begins. `R` and `F5` replace gameplay atomically without touching the camera, after which normal fixed steps target the new player. The same update remains active in `PlayerDead` and `AllBirdsDead` states.

`CameraView` derives orthographic bounds from the current logical center. Vertical span is always exactly `10`; horizontal span is `10 × viewportWidth / viewportHeight` (approximately `13.333333` at 4:3 and `17.777778` at 16:9). Projection is recomputed from the current MonoGame viewport every draw, including after a window resize, and never inspects cave bounds. Simulation coordinates remain world coordinates.

### Presentation layers and assets

`Presentation/SpritePresentation.cs` owns SpriteBatch concerns separately from `Camera2D`. It exposes point-clamped screen and world sprite foundations, a world-to-screen transform matching the primitive camera, and a required-content loader that adds the asset name and content root to load failures. Milestone 7 needs no MGCB asset: a standalone-generated one-pixel texture fills the viewport through SpriteBatch as a screen-attached backdrop. It stays visually fixed while the camera moves and does not parallax.

The explicit draw order is: generated background; cave terrain; enemy/world entity placeholders; player placeholder; projectile/foreground placeholders; terrain outline, collision/facing, and navigation debug overlays. Existing primitive gameplay/debug rendering remains world-aligned and debug lines draw last for visibility.

Milestone 8 supersedes the temporary asset/placeholder statements above. The camera motion and framing remain unchanged; MonoGame renders the latest fixed-step camera state without adding render-time interpolation.

## Original sprite presentation and animation

### Imported visual assets

The user explicitly approved importing the active original visual assets. Only these PNGs were copied; no `.meta`, Animator, animation, audio, effect, or unrelated Sunnyland content was imported:

- `Assets/sprites/adventurer-v1.5-Sheet.png` → `src/BirdHell/Content/Sprites/Player/adventurer-v1.5-Sheet.png`
- `Assets/sprites/eagle-attack-1.png` through `eagle-attack-4.png` → `src/BirdHell/Content/Sprites/Enemy/`
- `Assets/sprites/sword-attack-animation.png` → `src/BirdHell/Content/Sprites/Projectile/sword-attack-animation.png`
- `Assets/Sunnyland/artwork/Environment/back.png` → `src/BirdHell/Content/Sprites/Background/back.png`

The project copies these standalone-owned content files beside the executable and loads them directly with `Texture2D.FromStream`. Every required texture is loaded at startup; failures identify both the deterministic texture ID and resolved file path and abort rather than silently restoring a primitive placeholder. Sampling follows the audited importer policy described below.

### Renderer-independent sprite model

`SpriteRegion` stores a texture ID, pixel rectangle and coordinate origin, Unity-normalized pivot, pixels per unit, object scale, and filter policy. `SpriteFrame`, `SpriteAnimationClip`, and `AnimationPlayback` store frame durations and looping behavior without requiring a graphics device. `BirdHellVisuals` contains the audited Bird Hell metadata. `PlayerAnimationState` and `WorldAnimationState` retain only clips, clocks, enums, and value snapshots; no player, enemy, projectile, or run reference survives a reset.

Unity's 100 PPU semantics are retained. Player frames are `50 × 37` with root scale `4`, producing `2.0 × 1.48` world units. Eagle frames are `40 × 41` with root scale `3`, producing `1.2 × 1.23`. These are render dimensions only; no gameplay collider, projectile sweep, spawn, navigation, or physics value changed.

### Animation metadata and repaired player policy

- Idle uses atlas frames `0–3`, loops at effective `12 × 0.35 = 4.2 fps`.
- Run uses `8–11`, loops at `12 fps`.
- Jump uses `12–15`, loops at effective `60 × 0.2 = 12 fps`.
- Roll/air-jump uses atlas frames `18,19,20,21,17` at 0.1 seconds per frame (10 fps), plays one non-looping 0.5-second cycle, and is triggered only by an accepted second jump.
- Crouch uses `4–7`, loops at effective `12 × 0.5 = 6 fps`.
- Attack uses `47,48,49,50,47`, is non-looping, and progresses at effective `60 × 1.5 = 90 fps`.
- Eagles use the four separate attack images at `12 fps`, a roughly `0.333`-second loop.
- Projectiles store the exact audited bottom-left Unity slices `_0 (0,3,68,56)`, `_1 (88,2,62,54)`, `_2 (171,3,52,36)`, and `_4 (246,5,43,16)`. The renderer converts bottom-left source coordinates to MonoGame's top-left coordinates only at draw time. They loop at effective `12 × 1.5 = 18 fps`, about `0.222` seconds per cycle, with bottom-left pivots.

The standalone intentionally repairs the defective Unity Animator graph. A shot starts or restarts Attack, which has highest priority and finishes after release. An actively playing accepted-second-jump Roll follows, then ordinary airborne Jump; a grounded player holding `S` or Down selects Crouch; horizontal speed strictly above `0.01` selects Run; and the remaining grounded state selects Idle. This does not reproduce Unity's unconditional Any State → Idle transition or its script bug that never made Jump visible. Crouch and Roll are presentation-only and change no movement, speed, physics, dimensions, jump count, or collider.

#### Unwired air-jump artwork audit

The checked-in Bird Hell project contains no roll/air-jump `.anim`, no roll Animator state, parameter, or transition, and no script call that activates such a state. Its active double-jump implementation only increments the jump counter and reapplies jump velocity. The active game therefore never displayed a roll.

The already-imported Adventurer atlas does contain the authored spin sequence. Correlating its checked-in sprite IDs and artwork identifies frames `18,19,20,21,17`, with Unity bottom-left rectangles `(200,481,50,37)`, `(250,481,50,37)`, `(300,481,50,37)`, `(0,444,50,37)`, and `(150,481,50,37)`. The matching authored `playerAirJump` sequence for this exact atlas uses 0.1-second key spacing, a source clip sample rate of 60, Animator state speed 1, and looping playback. Because that clip/controller is not part of Bird Hell and the standalone requirement is one complete roll per accepted second jump, Bird Hell Standalone uses the exact sequence/key progression as a non-looping 0.5-second presentation clip.

`PlayerMovementSystem.Step` returns a value-only `PlayerMovementStepResult` identifying whether a jump was accepted and its authoritative jump number. `MainScreenRun.Step` exposes only `SecondJumpPerformed` to the host. Presentation therefore starts Roll only for an actually executed jump number 2—not from Space input, generic airborne state, a rejected third jump, or walking off an edge. Landing, `R`, and `F5` clear Roll. Attack remains visually highest priority; if it interrupts Roll, the independent roll clock continues rather than restarting, then returns to Roll if still active or to Jump/grounded presentation after completion.

Player, enemy, and projectile rendering use horizontal sprite flipping from existing authoritative facing/direction values. Rendering never flips collision geometry, FirePoint/muzzle logic, trajectories, or navigation. The player is omitted when `PlayerDead`; death artwork/effects remain deferred. Existing damage-flash timers are still read by sprite tint selection and retain the current white flash; exact original red/timer parity is deferred.

### Host-test filtering and eagle-facing correction

Unity 2019.4 importer metadata uses `filterMode: 0` (explicit Point) for all four eagle images and `back.png`. The player and projectile atlases use `filterMode: -1`, meaning they inherit Unity's Sprite texture default, Bilinear. Milestone 8 initially opened one `PointClamp` world SpriteBatch for every entity, incorrectly applying Point to player and projectile artwork. The corrected renderer uses a Point world layer for eagles and a LinearClamp world layer for player/projectiles; the screen-attached background remains Point. No mipmaps are used, matching every active importer.

At 1280×720 the faithful 10-unit camera yields exactly 72 screen pixels per world unit. One source texel therefore covers 2.88 screen pixels for the scale-4 player, 2.16 for scale-3 eagles, 0.72 for scale-1 projectiles, and 7.2 for the scale-10 background. These are fractional ratios. World entity positions and the smoothed camera are not rounded; the background destination rectangle is rounded to integer pixels. Point sampling at fractional magnification makes uneven hard texel boundaries visible, which is expected for the explicitly Point-filtered eagle/background but was a mismatch for player/projectile. Dimensions, PPU, camera span, animation timing, and gameplay remain unchanged.

The native eagle artwork faces left. Original `birdGFX.cs` used scale `(+3,3,3)` for left and `(-3,3,3)` for meaningful positive desired X, although its second comparison accidentally used `<= +0.01` rather than `<= -0.01`. The initial standalone mapping assumed the texture faced right, so `FacingDirection = +1` applied no mirror and displayed a left-facing bird. It also selected facing from desired velocity before acceleration/collision, allowing a path turn to flip presentation before actual movement reversed.

The corrected mapping renders standalone right-facing state with horizontal mirroring and left-facing state unmirrored. `EnemyNavigationSystem` updates this presentation state from final post-collision horizontal velocity: `>= +0.01` faces right, `<= -0.01` faces left, and the dead band preserves the prior meaningful direction. This does not face toward the player directly and does not mutate pathfinding, target selection, acceleration, speed, collision, separation, combat, velocity, or path state.

### Background, ordering, and reset

`back.png` is screen/camera attached at local offset `(-2.66,-5)` with original scale `10`, for an effective `38.4 × 24` world-unit image. It is oversized rather than stretched to each viewport, point sampled, untiled, and has no parallax at either 4:3 or 16:9.

Draw order is background sprite, cave terrain, eagle sprites, player sprite, sword/slash projectile sprites, reserved future effects, then debug overlays. `O`, `C`, and `N` remain visible and functional; primitive player/enemy/projectile production shapes are gone. `R` and `F5` clear attack state, restart player/enemy clocks deterministically, and rely on ordinary current-key polling for crouch. Projectile animation derives from each authoritative projectile age, so clearing projectiles also clears every projectile visual without retaining entity references. Camera position remains untouched.

Impact and enemy/player death effects, audio, and exact Unity damage-flash color/timer parity remain explicitly deferred.
