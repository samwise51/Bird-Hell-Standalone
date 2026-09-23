# Bird Hell Milestone 10 session handoff

State updated 2026-09-23 UTC. This is the starting point for a **fresh OpenClaw/Codex session** and reflects inspected repository state through the Milestone 10G profile work and the subsequent enemy-navigation investigation.

## 1. Repository state

- Repository: `/home/node/.openclaw/workspace/bird-hell-standalone`
- Branch: `master`
- HEAD: `0af1ded462d8ac77f0bcc02fe4a1d12d7b06f673`
- Subject: `0af1ded Milestone 9: Add high-resolution alien desert environment`
- Milestones 10A-10G and the subsequent bird-navigation fix are deliberately **uncommitted** on top of that commit.

### Critical warning

**Preserve the entire working tree.** It is the only copy of completed Milestone 10 work and the navigation fix. Do not reset, restore, checkout, clean, or discard these files. Do not stage, commit, or push; the user intends to commit/push personally from the CachyOS host.

Modified tracked files:

```text
BirdHell.sln
src/BirdHell/Core/BirdHellGame.cs
src/BirdHell/Core/MainScreenRun.cs
src/BirdHell/Program.cs
src/BirdHell/Systems/AStarPathfinder.cs
src/BirdHell/Systems/EnemyNavigationSystem.cs
src/BirdHell/Systems/EnemySpawnSystem.cs
src/BirdHell/Systems/PlayerSpawnSystem.cs
src/BirdHell/Systems/TerrainCollisionSystem.cs
src/BirdHell/World/NavigationGrid.cs
tests/BirdHell.EnemyNavigationChecks/Program.cs
tests/BirdHell.EnvironmentPresentationChecks/Program.cs
tests/BirdHell.LifecycleChecks/Program.cs
tests/BirdHell.PlayerChecks/Program.cs
```

Untracked Milestone 10 files:

```text
docs/MILESTONE_10_TOPOLOGY_BASELINE.md
docs/milestone-10e/comparison-results.csv
docs/milestone-10e/comparison-summary.md
docs/milestone-10e/m10-quality-0034-baseline-vs-conservative.svg
docs/milestone-10e/m10-quality-0036-baseline-vs-conservative.svg
docs/milestone-10e/m10-quality-0053-baseline-vs-conservative.svg
docs/milestone-10e/m10-quality-0196-baseline-vs-conservative.svg
docs/milestone-10g/README.md
docs/milestone-10g/comparison-results.csv
docs/milestone-10g/comparison-summary.md
docs/milestone-10g/*.svg
src/BirdHell/Systems/TerrainOccupancy.cs
src/BirdHell/World/Analysis/CaveTopologyAnalyzer.cs
src/BirdHell/World/CaveGenerationProfiles.cs
tests/BirdHell.GenerationComparisonChecks/BirdHell.GenerationComparisonChecks.csproj
tests/BirdHell.GenerationComparisonChecks/Program.cs
tests/BirdHell.TopologyChecks/BirdHell.TopologyChecks.csproj
tests/BirdHell.TopologyChecks/Program.cs
```

This handoff is an additional untracked file. No known Milestone 10 code is half-written. Baseline remains the runtime default. Baseline, conservative-irregular, dense, complex, and extreme are selectable through the development `--cave-profile` CLI, and R/F5 retain the selected profile. There is no generation retry/fallback. The 10E and 10G comparison runners rewrite their CSV/Markdown/SVG outputs, including timing fields.

The final sentence of `MILESTONE_10_TOPOLOGY_BASELINE.md` mentions measuring authored shelves and is obsolete. The user explicitly chose to skip wall-anchored shelves and all artificial platforms.

## 2. Latest update — runtime profiles and bird navigation

This section supersedes the old “Immediate next task” plan near the bottom of this document. The detailed 10A-10E record below remains useful historical context.

### Milestones 10F and 10G now present

- Runtime profile selection is implemented through `--cave-profile` for `baseline`, `conservative-irregular`, `dense`, `complex`, and `extreme`; omitted selection remains baseline.
- Dense is exactly `42 / 4 / 40 / 50 / 4`, Complex `45 / 3 / 35 / 50 / 3`, and Extreme `48 / 3 / 35 / 50 / 3` for initial wall fill / smoothing passes / wall threshold / room threshold / passage radius.
- The 10G offline comparison evaluates all five profiles over the same 256 deterministic seeds with independent generation RNG, records failures without retries/filtering, and emits compact CSV/Markdown/SVG artifacts under `docs/milestone-10g/`.
- All five profiles completed all 256 generation/topology/player/15-bird spawn pipelines in the recorded comparison. This proves initial validity, not successful dynamic pursuit.
- R selects a new seed while retaining the profile. F5 reconstructs the same seed and profile. Profile parsing/lifecycle tests cover defaulting, validation, propagation, deterministic reconstruction, and failed replacement.
- Baseline remains the default. No generator algorithm, cave parameters, collision size, artificial shelf, or platform was added by the navigation work.

### Confirmed waypoint-clearance defect and fix

The dynamic pursuit investigation found a concrete mismatch between path planning and steering:

1. A* produced paths whose graph nodes met the navigation grid's `0.5`-unit clearance requirement.
2. `EnemyNavigationSystem.AdvanceWaypoint` skipped any current waypoint within the inherited `2`-unit look-ahead distance.
3. It did not check whether the straight chord from the bird's actual, potentially off-grid collider center to the following waypoint was terrain-clear.
4. Near sloped walls and inside corners, that chord could intersect terrain even though every A* waypoint was valid. The bird then accelerated repeatedly into the wall while the N overlay still displayed a valid path beyond it.

Authored vertical-wall, L-corner, and diagonal-wall searches reproduced blocked steering targets before the fix, with circle-sweep collision fractions around `0.35-0.55` in representative cases.

The targeted fix is in `EnemyNavigationSystem.AdvanceWaypoint`: a waypoint may be skipped only when `TerrainCollisionSystem.TrySweepCircle` confirms that the chord to the next waypoint is clear for the **actual bird collision radius**. A hit before the destination keeps the intermediate waypoint. There is no teleport, collision bypass, navigation-grid relaxation, or generation change. `TerrainCollisionSystem.MoveCircle` now returns whether movement collided so diagnostics can report recent contact; its resolution behavior is otherwise unchanged.

### Complex and Extreme observations/results

Original gameplay observations supplied by the user:

- Complex seed `1790185384225-daf1e4fe43747dca33459c62f419151`: several birds clustered against a sloped ceiling near a narrow passage and did not route toward the player.
- Extreme seed `1790185660588-dfad85a8bf5b41cea2b67344d7e93f`: birds could remain pressed against a wall despite a visually clear route and visible navigation path.

Those strings are not exact examples of the current randomly generated seed format: the current form is a 13-digit timestamp, a hyphen, and 32 lowercase GUID hex characters, while the supplied suffixes have 31 and 30 characters. Seeds are opaque deterministic strings to the generator, so both supplied strings remain valid and were exercised exactly as provided.

Post-fix renderer-independent simulations constructed the full Complex and Extreme runs and stepped all 15 actual birds for 1,500 fixed 20 ms frames toward the stationary spawned player. For both supplied strings:

- every bird accumulated more than 5 units of movement;
- no bird ended intersecting terrain;
- no bird remained both more than 3 units from the player and stalled for at least 0.5 seconds.

Focused tests also exercise route completion around a wall and five-bird pursuit through a narrow authored passage without terrain penetration or a permanent pre-passage cluster. These are deterministic simulation results, not a claim that post-fix graphical playtesting has been completed on CachyOS.

### Remaining bird-crowding problem

The waypoint fix addresses wall-directed steering, but does not solve endpoint crowding. During diagnosis, an initially over-strict assertion classified birds about `2.1-2.9` units from a stationary player as failures. Diagnostics showed `2-4` nearby birds and no recent terrain collision: they had reached the player area and were mutually crowding, rather than being stranded behind a wall.

Current separation is deliberately modest: two deterministic pairwise overlap-correction passes after movement. It prevents most overlap but is not reciprocal-velocity-obstacle steering, queueing, flocking, or coordinated passage reservation. Multiple birds can therefore bunch around the player or temporarily impede one another at a constrained endpoint. Do not “fix” this by weakening terrain collision, shrinking colliders, teleporting, or changing cave parameters. A later crowding change needs a focused behavior/design decision and tests that distinguish useful surrounding pressure from permanent deadlock.

### Development-only stall diagnostics

With N enabled, the overlay/title now exposes the most relevant living bird's ID, position, velocity, current waypoint, actual last-step movement, signed progress toward the player, player distance, navigation component, recent terrain collision, nearby-bird count, and stall duration. Orange marks recent terrain collision; red marks at least 0.5 seconds of detected distant stall. Normal gameplay remains uncluttered.

Use these diagnostics during the next host session to determine whether any remaining failure is wall contact (orange/collision), no actual movement (red/stall), lack of player-distance progress, a component mismatch, or nearby-bird crowding.

## 3. Completed milestones through 10E (historical detail)

## 10A — Shared terrain occupancy and corrected navigation

### Occupancy API and coordinates

`src/BirdHell/Systems/TerrainOccupancy.cs` defines `TerrainPointClassification` (`Exterior`, `Solid`, `Traversable`) and `TerrainOccupancy(CaveGeometry)`:

- `Classify`, `IsExterior`, `IsSolid`, `IsTraversable`
- `HasClearance`, `GetBoundaryClearance`, `IntersectsCircle`
- `Geometry` and `BoundarySegments`

Marching-squares geometry is authoritative. Points inside filled `CaveGeometry.Triangles` are solid; points inside `MinX..MaxX`/ `MinY..MaxY` but outside triangles are traversable; points outside those bounds are exterior. Point tolerance is `0.0001f`. Clearance uses outline segments only after confirming the query center is traversable, fixing the bug where wall interiors/exterior could be far from outlines and marked walkable.

World coordinates are centered. `CaveGeometryBuilder` adds a one-tile wall border, uses square size `1f`, and computes symmetric bordered bounds. Logical tile `(x,y)` has center:

```text
(geometry.MinX + (x + 1) * squareSize,
 geometry.MinY + (y + 1) * squareSize)
```

`TerrainCollisionSystem` accepts shared occupancy; its geometry constructor delegates through it. Segment-only construction remains for authored tests. Constants remain skin `0.0025f`, ground probe `0.035f`, and maximum walkable slope `45f`.

### Navigation and A*

`NavigationGrid` retains width 128, height 72, node size 1, center zero, and scan radius `0.5f`. `NavigationGrid(CaveGeometry)` delegates to `NavigationGrid(TerrainOccupancy)`; geometry-backed cells require a traversable center and `HasClearance(center, scanRadius)`. Solid and exterior centers are blocked.

The generic `NavigationGrid(IEnumerable<TerrainSegment>, ...)` deliberately retains segment-distance-only semantics and does not infer inside/outside.

- `NoComponent = -1`.
- IDs are deterministic: row-major seed-cell scan plus BFS using stable neighbor order.
- APIs: `GetComponentId`, `AreConnected`, `TryFindNearestWalkable`, `TryFindNearestWalkableInComponent`, `TryGetComponentForPosition`.
- Nearest lookup scans row-major, giving stable ties.
- Diagonals require both adjacent cardinal cells walkable (no corner cutting).
- Component IDs apply only to that exact grid/map.

`AStarPathfinder.FindPath` rejects non-traversable world endpoints and disconnected projected cells. Priority `(total, heuristic, index)` and stable grid traversal preserve determinism.

Limitations: the 1-unit graph/0.5 clearance is not jump reachability or full steering proof. `IsSolid` does not mean “solid or exterior”; callers needing that distinction should use `Classify`/`IsTraversable`. Segment-only grids cannot classify inside/outside by design.

## 10B — Cave topology and quality analyzer

`src/BirdHell/World/Analysis/CaveTopologyAnalyzer.cs` defines:

```csharp
CaveTopologyReport Analyze(
    CaveMap map,
    CaveGeometry geometry,
    TerrainOccupancy? occupancy = null,
    NavigationGrid? navigationGrid = null,
    CaveTopologyAnalysisOptions? options = null)
```

Public models are `CaveTopologyAnalysisOptions`, `CaveQualityThresholds`, `LogicalComponentReport`, `FloorSurfaceReport`, `FloorRunReport`, `LandingSiteReport`, `NavigationComponentReport`, `CaveValidationFailureCode`/`CaveValidationFailure`, and `CaveTopologyReport`.

Defaults: landing samples every `0.25f`; minimum analysis support is `PlayerCollider.Original.Width`; 8 horizontal and 8 elevation bands; approximate bottleneck clearance `1.5f`; landing navigation lookup distance `2f`.

Metrics/semantics:

- Four-neighbor logical open-tile components; main logical component is largest tile count then lowest ID.
- Logical count/coverage, physical player-capsule clearance, and navigation connectivity are separate.
- Outline edges are probed to identify the open-facing normal. Upward surfaces with `normal.Y >= cos(45°)` are walkable and contiguous edges form floor runs.
- Landing sites sample qualifying runs, inset by capsule radius, place the original capsule with collision skin, require no penetration and actual grounded contact, then record logical/navigation IDs, support, headroom, and bands.
- Landing order is support descending, headroom descending, surface X/Y ascending, floor-run ID ascending; rank is assigned afterward.
- Navigation reports contain component cell count, enemy-circle-clear count, and world bounds.
- Approximate bottlenecks have 2-4 valid neighbors and <=1.5 units boundary clearance. They are not articulation points.

Default hard failures are `NoOpenSpace`, `NoLogicalComponent`, `NoPlayerCapsuleClearLocation`, `NoGroundedLandingSite`, and `NoEnemyClearNavigationCell`. Nullable `CaveQualityThresholds` are off by default and, when enabled, report exact observed/required values. They are calibration rules, not silent validity requirements.

Fixed corpora:

- `m10-smoke-0000..0015` (16)
- `m10-quality-0000..0255` (256)
- Details: `docs/MILESTONE_10_TOPOLOGY_BASELINE.md`

Baseline 256-seed results:

| Metric | Min | Median | Max | Mean |
|---|---:|---:|---:|---:|
| Open space % | 76.2500 | 82.0573 | 85.7552 | 81.8691 |
| Main logical share % | 100 | 100 | 100 | 100 |
| Navigation components | 1 | 1 | 2 | 1.0039 |
| Player/enemy-clear navigation cells | 2637 | 2860 | 3004 | 2849.7383 |
| Floor runs | 2 | 6 | 12 | 6.2031 |
| Per-map median floor width | 1 | 3 | 47 | 4.1602 |
| Platform-scale runs (1-12 units) | 1 | 4 | 9 | 4.4180 |
| Sloped floor surfaces | 25 | 35 | 44 | 34.8984 |
| Landing sites | 387 | 436 | 501 | 435.5352 |
| Landing-bearing runs | 1 | 5 | 12 | 5.5859 |
| Occupied elevation bands | 2 | 5 | 7 | 5.0352 |
| Elevation span | 4.0298 | 22.0000 | 25.8408 | 20.8030 |
| Approximate bottlenecks | 64 | 86 | 121 | 87.3867 |

All 256 pass default hard validity and validated spawning. `m10-quality-0117` is the baseline outlier: one logical component but navigation components of 2820 and 6 cells.

Authored tests cover empty, nearly full, disconnected, narrow corridor, bottleneck, shelf-shaped, and multi-elevation maps. The shelf fixture tests analysis only and is not a generation-roadmap item.

## 10C — Topology-aware player spawning

`PlayerSpawnSystem.cs` defines `PlayerSpawnOptions`, `PlayerSpawnRejectionReason`, `PlayerSpawnSelection`, `PlayerSpawnFailure`, `PlayerSpawnResult`, `PlayerSpawnException`, and `PlayerSpawnSystem.Select`. It consumes existing analyzer landing sites plus shared occupancy/collision/navigation.

Original collider is unchanged:

- Width `0.292390256`
- Height `1.175637`
- Offset approximately `(0.011323646, -0.13135469)`

Default hard filters:

- Main logical component membership.
- Continuous support >= `1f`.
- Headroom >= skin (`0.0025f`).
- Exact full-capsule nonpenetration.
- Fresh contact probe grounded on <=45° support; analyzer normal must also pass.
- Explicit landing navigation component and nearest cell within `2f`.
- Global and component-constrained nearest lookup must agree on cell/component.
- Component >=32 cells and >=15 enemy-clear cells.
- Local BFS within 4 units >=12 cells.

Failure is structured with candidate count, main logical ID, rejection counts, and message; there is no unchecked fallback.

Survivor ranking: enemy-clear component cells, component cells, local cells, support, and headroom descending; distance to legacy preferred region ascending; then position X/Y, floor-run ID, landing rank. The legacy preferred point (largest-outline bounds center minus `(5,5)`) is only a late preference.

`PlayerSpawnSelection` exposes position/contact, chosen nav cell and IDs, component/enemy-clear/local counts, support/headroom, preferred distance, landing rank, and floor run. `MainScreenRun.PlayerNavigationComponentId` returns the verified component, never the assumed largest nav component.

Results: smoke 16/16 and baseline 256/256, no failures. Selected component cells min/median/max 2637/2860/3004; local cells 34/37/39. Same-seed/F5 reproduces exact position/component. The graph still does not prove jump traversal; limits remain configurable.

## 10D — Distributed 15-enemy spawning

`EnemySpawnSystem.cs` defines `EnemySpawnOptions`, failure code/model/result/exception types, `EnemySpawnPlacement`, `EnemySpawnSelection`, and `Select`; `OriginalEnemyCount = 15`.

Candidates are nav-cell centers in the verified player component, connected to the player cell, geometrically traversable, clear for the original enemy circle, and nonoverlapping with the player capsule. Enemy radius remains `0.45807048f`, collider offset `(-0.15384216f,-0.07692099f)`. Hard pairwise separation is `2*radius + 0.01 = 0.92614096`.

Candidates sort by center X/Y then cell X/Y. First selection is closest to the preferred 10-unit player distance among candidates >=8 units away (distance is soft). Later selections maximize minimum selected distance, then prefer the 8-12 player band, closeness to 10, and coordinates. Terrain, same-component connectivity, player nonoverlap, and hard pairwise separation are never relaxed.

Exactly 15 are required. Failures are `InvalidPlayerNavigationComponent`, `NoCollisionSafeCandidates`, or `InsufficientSeparatedCapacity`, with required/candidate/selected counts, component, hard separation, and message. No partial population is published. Diagnostics include actual spacing/player distance, spans, bands, and candidate count.

Placement consumes no RNG. Speed is independently `2 + FNV1a("enemy-speed:v1:{seed}:{enemyId}") % 8`, preserving speeds 2-9 regardless of placement scoring.

Results: smoke 16/16 and baseline 256/256. Candidate min/median/max 2636/2859/3003; minimum spacing 13.0384/15.0333/17; closest player distance 8.0134/9.9952/10.0222. F5 repeats placement/speeds. Grid-center quantization and graph-vs-dynamic-movement remain limitations.

## 10E — Conservative generation comparison

`CaveGenerationProfiles.cs` adds:

| Profile | Size | Fill | Smooth | Wall threshold | Room threshold | Passage radius |
|---|---:|---:|---:|---:|---:|---:|
| `Baseline` | 128x30 | 35% | 5 | 50 | 50 | 5 |
| `ConservativeIrregular` | 128x30 | 38% | 4 | 40 | 50 | 4 |

`CaveGenerationOptions` and `CaveGenerator` were not changed. `Baseline => new()` matches actual defaults, and `MainScreenRun` still has `new CaveGenerator()`; gameplay remains baseline.

Generator order: bordered random fill, in-place cellular smoothing, small-wall removal, small-open-room filling, room sorting/main marking, closest-room connection with radius-carved line passages. Cave seed hashing is stable FNV-1a into `System.Random`.

`BirdHell.GenerationComparisonChecks` validates profiles/defaults, alternates evaluation order, and runs map -> geometry -> occupancy -> corrected nav -> topology -> validated player -> validated 15 enemies for identical 256 seeds/profile with no retries.

Artifacts:

- `docs/milestone-10e/comparison-results.csv` (all 512 outcomes)
- `docs/milestone-10e/comparison-summary.md`
- SVGs for seeds `0036`, `0196`, `0053`, and `0034`

Both profiles completed **256/256 full pipelines** with no generation/topology/player/enemy failure.

| Metric | Baseline min / median / max / mean | Conservative min / median / max / mean |
|---|---:|---:|
| Open % | 76.2500 / 82.0573 / 85.7552 / 81.8691 | 70.3385 / 78.3073 / 84.1667 / 78.2953 |
| Logical components | 1 / 1 / 1 / 1 | 1 / 1 / 1 / 1 |
| Navigation components | 1 / 1 / 2 / 1.0039 | 1 / 1 / 3 / 1.0508 |
| Navigation cells | 2637 / 2860 / 3004 / 2849.7383 | 2372 / 2706 / 2943 / 2702.9023 |
| Floor runs | 2 / 6 / 12 / 6.2031 | 3 / 7 / 15 / 7.6016 |
| Per-map median floor width | 1 / 3 / 47 / 4.1602 | 1 / 5 / 39 / 5.9336 |
| Platform-scale runs | 1 / 4 / 9 / 4.4180 | 1 / 5 / 12 / 5.1523 |
| Sloped floors | 25 / 35 / 44 / 34.8984 | 25 / 38 / 46 / 37.6992 |
| Landing sites | 387 / 436 / 501 / 435.5352 | 391 / 438 / 515 / 440.2227 |
| Landing-bearing runs | 1 / 5 / 12 / 5.5859 | 2 / 7 / 15 / 7.0703 |
| Elevation bands | 2 / 5 / 7 / 5.0352 | 3 / 6 / 8 / 5.6328 |
| Elevation span | 4.0298 / 22 / 25.8408 / 20.8030 | 9.2198 / 22.3966 / 25.7247 / 21.6881 |
| Bottlenecks | 64 / 86 / 121 / 87.3867 | 74 / 100 / 147 / 100.9727 |
| Enemy candidates | 2636 / 2859 / 3003 / 2848.7383 | 1872 / 2703 / 2942 / 2699.4766 |
| Enemy spacing | 13.0384 / 15.0333 / 17 | 12.8062 / 14.8661 / 16.7631 |
| Closest player distance | 8.0134 / 9.9952 / 10.0222 | 8.0282 / 9.9947 / 10.0946 |

Conservative median deltas: floor runs +1, floor width +2, platform-scale runs +1, sloped floors +3, landings +2, landing-bearing runs +2, elevation bands +1, elevation span +0.3966, bottlenecks +14, navigation cells -154. This supports interactive testing for more natural variety; it is not yet a gameplay-default decision.

Conservative multi-nav-component seeds: `0047`, `0050`, `0066`, `0097`, `0099`, `0101`, `0140`, `0146`, `0206`, `0207` (3 components), `0227`, `0247`.

Old baseline-specific thresholds reject 0 baseline and 26 conservative seeds. All 26 are below 75% open; six also have <2500 clear nav cells: `0047`, `0067`, `0147`, `0185`, `0187`, `0208`. These are not hard/spawn failures.

Host timing (directional, not benchmark):

| Timing ms | Baseline min / median / max / mean | Conservative min / median / max / mean |
|---|---:|---:|
| Generation | 3.6800 / 4.2360 / 9.5659 / 4.2873 | 3.2319 / 3.6786 / 5.6092 / 3.7089 |
| Full pipeline | 1041.6512 / 1174.2498 / 1546.4889 / 1178.1346 | 1054.0475 / 1205.0374 / 1517.7123 / 1214.8039 |

Generation mean was ~13.5% lower; full pipeline mean ~3.1% higher (median ~2.6%). Recommendation: controlled conservative host testing, baseline still default, no artificial platforms.

## 4. Testing

Solution test projects:

1. GenerationChecks
2. PlayerChecks
3. CombatChecks
4. EnemyNavigationChecks
5. LifecycleChecks
6. CameraPresentationChecks
7. SpritePresentationChecks
8. EnvironmentPresentationChecks
9. TopologyChecks
10. GenerationComparisonChecks

Latest verification after the waypoint-clearance fix:

- `dotnet restore BirdHell.sln`: passed.
- `dotnet build BirdHell.sln --no-restore`: 0 warnings/errors.
- All test projects except the expensive five-profile `GenerationComparisonChecks` passed: Generation, Player, Combat, EnemyNavigation, Lifecycle, CameraPresentation, SpritePresentation, EnvironmentPresentation, and Topology.
- `EnemyNavigationChecks` includes the radius-clear look-ahead regressions, wall-route completion, multi-bird narrow-passage pursuit, diagnostics, and 1,500-frame Complex/Extreme supplied-seed simulations described above.
- `TopologyChecks` ran its smoke and full 256-seed corpus with zero hard failures and 256/256 player and enemy spawn success.
- `git diff --check`: passed.

The five-profile 256-seed comparison was deliberately not rerun after the navigation fix because generation, topology, and spawn logic were unchanged. Its previously generated 1,280 full-pipeline outcomes remain under `docs/milestone-10g/`. The current handoff edit itself did not rerun build/tests. No known automated failure remains, but post-fix graphical Complex/Extreme playtesting and the separate endpoint-crowding design decision remain open.

Reproduce:

```bash
dotnet restore BirdHell.sln
dotnet build BirdHell.sln --no-restore

set -e
find tests -mindepth 2 -maxdepth 2 -name '*.csproj' | sort | while IFS= read -r project; do
  dotnet run --no-build --project "$project"
done

git diff --check
git status --short
```

Focused corpora:

```bash
dotnet run --no-build --project tests/BirdHell.TopologyChecks/BirdHell.TopologyChecks.csproj
dotnet run --no-build --project tests/BirdHell.GenerationComparisonChecks/BirdHell.GenerationComparisonChecks.csproj
```

The comparison command rewrites `docs/milestone-10g/*`; timing diffs are expected. Do not rerun it merely to verify the steering-only change.

## 5. Architecture and invariants

Important paths:

- Generation: `World/CaveGenerationOptions.cs`, `CaveGenerationProfiles.cs`, `CaveGenerator.cs`, `CaveMap.cs`, `Room.cs`
- Geometry: `World/Geometry/CaveGeometryBuilder.cs`, `CaveGeometry.cs`
- Occupancy/collision: `Systems/TerrainOccupancy.cs`, `TerrainCollisionSystem.cs`
- Navigation: `World/NavigationGrid.cs`, `Systems/AStarPathfinder.cs`
- Analysis/spawning: `World/Analysis/CaveTopologyAnalyzer.cs`, `Systems/PlayerSpawnSystem.cs`, `EnemySpawnSystem.cs`
- Lifecycle/host: `Core/MainScreenRun.cs`, `Core/BirdHellGame.cs`, `Program.cs`
- Environment/clouds: `Presentation/EnvironmentLayout.cs`, `CloudAtmosphere.cs`

`MainScreenRun.StartRun` constructs locals in this order: map, geometry, occupancy, collision, movement, geometry-backed nav, topology, validated player selection, player/contact refresh, validated enemy selection, population, A* navigation, projectiles, weapon state. Only then does one assignment block publish `Seed` and every world/system property. Exceptions leave the old run intact.

Determinism/reset:

- Cave seed -> stable FNV-1a -> `System.Random`.
- Analyzer and placements consume no shared RNG.
- Enemy speed is separate versioned per-seed/per-ID hashing.
- R/`StartNewRun`: timestamp/GUID seed, forced different from current.
- F5/`RestartSameSeed`: `StartRun(Seed)`.
- Host reset occurs only after successful replacement, clears simulation/pending input, rebuilds render data and deterministic scenery.
- `EnvironmentSeedDerivation` uses independent 64-bit FNV-1a seed+versioned-layer streams.
- `CloudAtmosphereState` has fixed visual seed and an elapsed presentation clock; `OnGameplayReset()` intentionally does nothing. R/F5 preserve cloud time/visibility.

Preserve local-build-then-publish atomics, RNG separation, R/F5 semantics, cloud continuity, and all Milestone 9 artwork.

## 6. Immediate next work

1. Perform post-fix graphical gameplay on Complex and Extreme, first trying the two supplied strings if available from host notes and then new exact-format seeds.
2. Toggle N when a bird appears stuck. Record the complete seed/profile and title diagnostic values; use F5 to confirm same-seed/profile reproduction.
3. Verify that birds now follow intermediate waypoints around sloped ceilings and corners instead of displaying a path while steering through terrain.
4. Separately assess endpoint and narrow-passage crowding. Note whether a bird has recent terrain collision, whether player distance is decreasing, and how many nearby birds are reported. Do not conflate a group already within roughly 3 units of the player with a distant wall stall.
5. If crowding is judged harmful, define the desired combat behavior before changing separation/steering. Add a deterministic failing simulation for the chosen behavior first.
6. Before final handoff/commit, rerun the complete practical suite, build, `git diff --check`, and host regressions. Rerun the expensive five-profile comparison only if generation/topology/spawning changes.

Host launch commands (fish-compatible):

```fish
cd /home/node/.openclaw/workspace/bird-hell-standalone
dotnet run --project src/BirdHell/BirdHell.csproj -- --cave-profile complex
dotnet run --project src/BirdHell/BirdHell.csproj -- --cave-profile extreme
```

Explicit exclusion: **do not implement or propose wall-anchored shelves or artificial platforms.** Terrain variety must come from generator parameters alone.

## 7. Safety checklist

- Compare initial `git status --short` with this inventory.
- Never reset/restore/clean/checkout away current Milestone 10 or navigation work.
- Keep baseline as the runtime default.
- Preserve colliders, movement, combat, steering, camera, presentation, and M9 environment.
- Preserve atomic publication, deterministic spawning, R/F5, visual RNG isolation, and cloud continuity.
- Do not add generation retries without a separately reviewed requirement and deterministic design.
- Do not stage, commit, or push; the user handles those on CachyOS.
