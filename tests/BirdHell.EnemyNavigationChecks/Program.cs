using System.Numerics;
using System.Reflection;
using BirdHell.Core;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Analysis;
using BirdHell.World.Geometry;

const float step = 0.02f;
const float tolerance = 0.0001f;

var generatedSeedParts = CaveGenerator.CreateRandomSeed().Split('-');
Assert(generatedSeedParts.Length == 2 && generatedSeedParts[0].Length == 13 &&
       generatedSeedParts[0].All(char.IsAsciiDigit) && generatedSeedParts[1].Length == 32 &&
       generatedSeedParts[1].All(char.IsAsciiHexDigit),
    "Random cave seeds no longer use the 13-digit timestamp plus 32-hex GUID format.");
Pass("current random-seed format is explicit and stable");

var defaultGrid = new NavigationGrid([]);
Assert(defaultGrid.Width == 128 && defaultGrid.Height == 72 && defaultGrid.NodeSize == 1f, "Navigation dimensions differ from the GridGraph.");
Assert(defaultGrid.Minimum == new Vector2(-64f, -36f), "Navigation grid is not centered at the world origin.");
Assert(defaultGrid.CellToWorld(new NavigationCell(0, 0)) == new Vector2(-63.5f, -35.5f), "Navigation cell-center alignment is incorrect.");
Pass("navigation grid dimensions and world alignment");

var outlineGrid = Grid([new TerrainSegment(new Vector2(0f, -2f), new Vector2(0f, 2f))], scanRadius: 0.5f);
Assert(!outlineGrid.IsWalkable(3, 3), "A node touching authored outline geometry was not blocked.");
Assert(outlineGrid.IsWalkable(1, 3), "A distant node was incorrectly blocked.");
Pass("obstacles derive from outline segments with the 0.5 scan radius");

var openGrid = Grid([], 5, 5, 0f);
Assert(openGrid.GetNeighbors(new NavigationCell(2, 2)).Count == 8, "Open-space navigation is not eight-way.");
Pass("eight-way connectivity");

var cornerGrid = Grid([
    new TerrainSegment(new Vector2(0.9f, 0f), new Vector2(1.1f, 0f)),
    new TerrainSegment(new Vector2(0f, 0.9f), new Vector2(0f, 1.1f)),
], 5, 5, 0.15f);
Assert(!cornerGrid.GetNeighbors(new NavigationCell(2, 2)).Contains(new NavigationCell(3, 3)), "Diagonal movement cut a blocked corner.");
Pass("diagonal corner-cut prevention");

var aroundGrid = Grid([new TerrainSegment(new Vector2(0f, -2f), new Vector2(0f, 2f))], 7, 7, 0.15f);
var aroundFinder = new AStarPathfinder(aroundGrid);
var pathOne = aroundFinder.FindPath(new Vector2(-3f, 0f), new Vector2(3f, 0f));
var pathTwo = aroundFinder.FindPath(new Vector2(-3f, 0f), new Vector2(3f, 0f));
Assert(pathOne is { Count: > 0 } && pathTwo is not null && pathOne.SequenceEqual(pathTwo), "Identical A* input was not deterministic.");
Assert(pathOne!.Any(point => MathF.Abs(point.Y) >= 3f), "A* did not route around the authored obstacle.");
Pass("deterministic A* and routing around an authored obstacle");

var dividedGrid = Grid([new TerrainSegment(new Vector2(0f, -4f), new Vector2(0f, 4f))], 7, 7, 0.15f);
Assert(new AStarPathfinder(dividedGrid).FindPath(new Vector2(-3f, 0f), new Vector2(3f, 0f)) is null,
    "A* returned a partial path for an unreachable target.");
Pass("unreachable targets return no partial path");

var authoredGeometry = new CaveGeometryBuilder().Build(CreateTwoChamberMap());
var authoredOccupancy = new TerrainOccupancy(authoredGeometry);
var leftInterior = new Vector2(-3.5f, 0.5f);
var rightInterior = new Vector2(2.5f, 0.5f);
var solidInterior = new Vector2(0.5f, 0.5f);
var exterior = new Vector2(0.5f, 20.5f);
Assert(authoredOccupancy.Classify(leftInterior) == TerrainPointClassification.Traversable,
    "An authored chamber interior was not classified as traversable.");
Assert(authoredOccupancy.Classify(solidInterior) == TerrainPointClassification.Solid,
    "The interior of a substantial authored wall mass was not classified as solid.");
Assert(authoredOccupancy.Classify(exterior) == TerrainPointClassification.Exterior,
    "A point beyond the cave geometry was not classified as exterior.");
var authoredCollision = new TerrainCollisionSystem(authoredOccupancy);
Assert(authoredOccupancy.IntersectsCircle(solidInterior, 0.1f) &&
       authoredOccupancy.IntersectsCircle(exterior, 0.1f) &&
       authoredCollision.IntersectsCircle(solidInterior, 0.1f) &&
       authoredCollision.IntersectsCircle(exterior, 0.1f),
    "Geometry-backed collision did not reject a circle wholly inside solid or exterior space.");
Pass("shared occupancy distinguishes cave interior, solid wall interior, and exterior space");

var authoredGrid = new NavigationGrid(authoredGeometry);
Assert(authoredGrid.IsWalkable(authoredGrid.WorldToCell(leftInterior)),
    "A chamber cell with sufficient clearance was not walkable.");
Assert(!authoredGrid.IsWalkable(authoredGrid.WorldToCell(solidInterior)),
    "A cell deep inside a wall mass was walkable.");
Assert(!authoredGrid.IsWalkable(authoredGrid.WorldToCell(exterior)),
    "A cell outside the intended cave was walkable.");
var nearWallGrid = new NavigationGrid(authoredOccupancy, 1, 1, 1f, new Vector2(-1.75f, 0.5f), 0.5f);
Assert(authoredOccupancy.IsTraversable(new Vector2(-1.75f, 0.5f)) && !nearWallGrid.IsWalkable(0, 0),
    "A traversable point without the configured wall clearance remained walkable.");
Pass("geometry-backed navigation requires traversable occupancy and obstacle clearance");

Assert(authoredGrid.ComponentCount == 2, "The authored two-chamber cave did not produce two navigation components.");
var foundLeftComponent = authoredGrid.TryGetComponentForPosition(leftInterior, 2f, out var leftComponent, out var leftCell);
var foundRightComponent = authoredGrid.TryGetComponentForPosition(rightInterior, 2f, out var rightComponent, out var rightCell);
Assert(foundLeftComponent && foundRightComponent && leftComponent != rightComponent &&
       !authoredGrid.AreConnected(leftCell, rightCell),
    "Player-position component identification did not distinguish disconnected chambers.");
Assert(!authoredGrid.TryGetComponentForPosition(exterior, 100f, out _, out _),
    "Exterior space was associated with a playable navigation component.");
Assert(authoredGrid.TryFindNearestWalkableInComponent(rightInterior, 20f, leftComponent, out var constrainedOne) &&
       authoredGrid.TryFindNearestWalkableInComponent(rightInterior, 20f, leftComponent, out var constrainedTwo) &&
       constrainedOne == constrainedTwo && authoredGrid.GetComponentId(constrainedOne) == leftComponent,
    "Component-constrained nearest-cell lookup was not deterministic or escaped its component.");
var repeatedAuthoredGrid = new NavigationGrid(authoredGeometry);
Assert(repeatedAuthoredGrid.ComponentCount == authoredGrid.ComponentCount &&
       repeatedAuthoredGrid.GetComponentId(leftCell) == leftComponent &&
       repeatedAuthoredGrid.GetComponentId(rightCell) == rightComponent,
    "Navigation component identifiers were not stable for identical geometry.");
Pass("navigation components and constrained nearest-cell lookup are deterministic");

var authoredFinder = new AStarPathfinder(authoredGrid);
Assert(authoredFinder.FindPath(leftInterior, rightInterior) is null,
    "A* navigated through the solid wall separating authored chambers.");
Assert(authoredFinder.FindPath(leftInterior, exterior) is null,
    "A* projected an exterior destination back into the cave.");
var containedPath = authoredFinder.FindPath(new Vector2(-3.5f, -1.5f), new Vector2(-3.5f, 2.5f));
Assert(containedPath is { Count: > 0 } && containedPath.All(point => authoredOccupancy.HasClearance(point, authoredGrid.ScanRadius)),
    "A valid authored path escaped traversable cave space or violated clearance.");
Pass("A* cannot cross solid walls or escape the intended cave");

var generatedMap = new CaveGenerator().Generate("milestone-5-checks");
var geometry = new CaveGeometryBuilder().Build(generatedMap);
var generatedOccupancy = new TerrainOccupancy(geometry);
var generatedTerrain = new TerrainCollisionSystem(generatedOccupancy);
var generatedGrid = new NavigationGrid(generatedOccupancy);
var generatedWalkableCount = 0;
for (var y = 0; y < generatedGrid.Height; y++)
for (var x = 0; x < generatedGrid.Width; x++)
{
    var cell = new NavigationCell(x, y);
    if (!generatedGrid.IsWalkable(cell)) continue;
    generatedWalkableCount++;
    Assert(generatedOccupancy.HasClearance(generatedGrid.CellToWorld(cell), generatedGrid.ScanRadius),
        $"Generated walkable cell ({x}, {y}) is solid, exterior, or lacks clearance.");
}
Assert(generatedWalkableCount > 0, "Representative generated cave has no walkable gameplay cells.");
Pass("representative generated cave navigation contains only clear traversable cells");
var generatedTopology = new CaveTopologyAnalyzer().Analyze(
    generatedMap, geometry, generatedOccupancy, generatedGrid);
var generatedPlayerSpawn = PlayerSpawnSystem.Select(
    generatedTopology,
    geometry,
    generatedOccupancy,
    generatedTerrain,
    generatedGrid,
    PlayerCollider.Original).Selection!;
var generatedPlayer = new Player(generatedPlayerSpawn.Position);
generatedTerrain.RefreshContacts(generatedPlayer);
var generatedEnemySpawn = EnemySpawnSystem.Select(
    generatedOccupancy,
    generatedGrid,
    generatedPlayer,
    generatedPlayerSpawn,
    "milestone-5-checks");
Assert(generatedEnemySpawn.IsSuccess, generatedEnemySpawn.Failure?.Message ?? "Enemy spawn unexpectedly failed.");
var spawnSelection = generatedEnemySpawn.Selection!;
var spawned = spawnSelection.CreateEnemies();
Assert(spawned.Count == 15 && spawned.All(enemy => enemy.MaximumSpeed is >= 2 and <= 9), "Enemy count/speed range is incorrect.");
Assert(spawned.All(enemy => enemy.MaximumSpeed == (int)enemy.MaximumSpeed), "Enemy speeds are not integral.");
Assert(spawned.All(enemy => !generatedOccupancy.IntersectsCircle(enemy.ColliderCenter, enemy.ColliderRadius, 0f)),
    "Enemy spawn did not preserve full terrain-circle clearance.");
Assert(AllPairs(spawned).All(pair => Vector2.Distance(pair.First.ColliderCenter, pair.Second.ColliderCenter) >=
                                    spawnSelection.HardMinimumSeparation - tolerance),
    "Enemy spawn contains overlapping circles.");
Assert(spawned.All(enemy => !EnemyIntersectsPlayer(enemy, generatedPlayer, 0.01f)),
    "Enemy spawn overlaps the player capsule.");
var generatedFinder = new AStarPathfinder(generatedGrid);
Assert(spawnSelection.Placements.All(placement =>
        generatedGrid.GetComponentId(placement.NavigationCell) == generatedPlayerSpawn.NavigationComponentId &&
        generatedFinder.FindPath(placement.ColliderCenter, generatedPlayer.ColliderCenter) is { Count: > 0 }),
    "Enemy spawn escaped the player's navigation component or cannot reach the player.");
Assert(spawnSelection.HorizontalSpan > 50f && spawnSelection.VerticalSpan > 10f &&
       spawnSelection.OccupiedHorizontalBands >= 6 && spawnSelection.OccupiedVerticalBands >= 3,
    "Representative enemy spawn does not provide useful horizontal and vertical distribution.");
Pass("15 enemies are clear, nonoverlapping, reachable, and spatially distributed");

var repeatedEnemySpawn = EnemySpawnSystem.Select(
    generatedOccupancy, generatedGrid, generatedPlayer, generatedPlayerSpawn, "milestone-5-checks").Selection!;
var alternateSeedSpawn = EnemySpawnSystem.Select(
    generatedOccupancy, generatedGrid, generatedPlayer, generatedPlayerSpawn, "milestone-5-checks-alternate").Selection!;
Assert(spawnSelection.Placements.SequenceEqual(repeatedEnemySpawn.Placements),
    "Identical seed and topology did not reproduce enemy placement and speeds.");
Assert(spawnSelection.Placements.Select(item => item.ColliderCenter)
           .SequenceEqual(alternateSeedSpawn.Placements.Select(item => item.ColliderCenter)) &&
       !spawnSelection.Placements.Select(item => item.MaximumSpeed)
           .SequenceEqual(alternateSeedSpawn.Placements.Select(item => item.MaximumSpeed)),
    "Placement was coupled to speed RNG or alternate seed did not vary speed assignment.");
Pass("enemy placement and versioned per-enemy speed assignment are deterministic and decoupled");

var capacity = BuildSpawnContext(CreateSmallCapacityMap(), new PlayerSpawnOptions
{
    MinimumNavigationComponentCells = 1,
    MinimumEnemyClearNavigationCells = 0,
    MinimumLocalNavigationCells = 1,
});
Assert(capacity.PlayerSpawn.IsSuccess, "Small authored capacity map did not provide a player landing candidate.");
var capacityPlayer = new Player(capacity.PlayerSpawn.Selection!.Position);
capacity.Collision.RefreshContacts(capacityPlayer);
var capacityFailure = EnemySpawnSystem.Select(
    capacity.Occupancy,
    capacity.Navigation,
    capacityPlayer,
    capacity.PlayerSpawn.Selection,
    "insufficient-enemy-capacity");
Assert(!capacityFailure.IsSuccess &&
       capacityFailure.Failure!.Code == EnemySpawnFailureCode.InsufficientSeparatedCapacity &&
       capacityFailure.Failure.SelectedCount < EnemySpawnSystem.OriginalEnemyCount,
    "Insufficient-capacity cave did not return structured failure.");
Pass("narrow insufficient-capacity caves fail explicitly without unsafe fallback");

var emptyTerrain = new TerrainCollisionSystem([]);
var movementGrid = new NavigationGrid([], 16, 16);
var navigation = new EnemyNavigationSystem(new AStarPathfinder(movementGrid), emptyTerrain);
Assert(!navigation.EnemySeparationEnabled,
    "Bird-to-bird separation should be disabled so birds can overlap and pass through each other.");
var movingEnemy = AtCenter(0, new Vector2(-3.5f, 0.5f), 9);
navigation.Step([movingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(MathF.Abs(movingEnemy.Velocity.Length() - 0.2f) < tolerance, "Enemy acceleration did not use 10 units/sec².");
for (var index = 0; index < 200; index++) navigation.Step([movingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(movingEnemy.Velocity.Length() <= movingEnemy.MaximumSpeed + tolerance, "Enemy exceeded its randomized speed limit.");
Pass("enemy acceleration and maximum speed limiting");

var repathingEnemy = AtCenter(0, new Vector2(-3.5f, 0.5f), 5);
navigation.Step([repathingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(repathingEnemy.SuccessfulRepathCount == 1, "Enemy did not calculate its initial path.");
for (var index = 0; index < 24; index++) navigation.Step([repathingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(repathingEnemy.SuccessfulRepathCount == 2, "Enemy did not replan on the 0.5-second interval.");
Pass("0.5-second repath interval");

var endpointSegments = new[]
{
    new TerrainSegment(new Vector2(0f, -5f), new Vector2(0f, 5f)),
    new TerrainSegment(new Vector2(0.1f, -0.25f), new Vector2(5f, -0.25f)),
};
var endpointGrid = Grid(endpointSegments, 13, 11, 0.15f);
var endpointTerrain = new TerrainCollisionSystem(endpointSegments);
var generatedLookupEvidence = VerifyRadiusLookupEquivalence(generatedGrid, generatedTerrain, "generated cave");
var endpointLookupEvidence = VerifyRadiusLookupEquivalence(endpointGrid, endpointTerrain, "Bird 9 endpoint grid");
var oldExaminedCells = generatedLookupEvidence.OldExamined + endpointLookupEvidence.OldExamined;
var optimizedExaminedCells = generatedLookupEvidence.OptimizedExamined + endpointLookupEvidence.OptimizedExamined;
Assert(optimizedExaminedCells * 4 < oldExaminedCells,
    $"Nearest-first radius lookup did not substantially reduce examined cells ({optimizedExaminedCells} vs {oldExaminedCells}).");
Pass($"radius-clear nearest-first lookup matches row-major endpoints and examined " +
     $"{optimizedExaminedCells:N0} vs {oldExaminedCells:N0} cells");

var tieGrid = new NavigationGrid([], 4, 4, 1f, Vector2.Zero, 0f);
var tieTerrain = new TerrainCollisionSystem([]);
var tieNavigation = new EnemyNavigationSystem(new AStarPathfinder(tieGrid), tieTerrain);
var tieResult = InvokeRadiusLookup(tieNavigation, Vector2.Zero, 10f, null, Enemy.OriginalColliderRadius);
Assert(tieResult.Found && tieResult.Cell == new NavigationCell(1, 1),
    "Nearest-first radius lookup did not preserve row-major selection for an exact four-cell tie.");
Pass("radius-clear lookup preserves deterministic row-major tie-breaking");

var endpointNavigation = new EnemyNavigationSystem(new AStarPathfinder(endpointGrid), endpointTerrain);
var endpointEnemy = AtCenter(9, new Vector2(-3f, 1f), 5);
endpointEnemy.Path = [new Vector2(-3f, 1f)];
endpointEnemy.PathIndex = endpointEnemy.Path.Count;
var groundedTarget = new Vector2(2f, 0.15f);
var endpointStart = ReferenceRadiusLookup(
    endpointGrid,
    endpointTerrain,
    endpointEnemy.ColliderCenter,
    AStarPathfinder.MaximumNearestNodeDistance,
    null,
    endpointEnemy.ColliderRadius);
Assert(endpointStart.Found, "Bird 9 regression start no longer has a radius-clear endpoint.");
var endpointStartComponent = endpointGrid.GetComponentId(endpointStart.Cell);
var constrainedReference = ReferenceRadiusLookup(
    endpointGrid,
    endpointTerrain,
    groundedTarget,
    AStarPathfinder.MaximumNearestNodeDistance,
    endpointStartComponent,
    endpointEnemy.ColliderRadius);
var constrainedOptimized = InvokeRadiusLookup(
    endpointNavigation,
    groundedTarget,
    AStarPathfinder.MaximumNearestNodeDistance,
    endpointStartComponent,
    endpointEnemy.ColliderRadius);
Assert(constrainedOptimized.Found == constrainedReference.Found &&
       constrainedOptimized.Cell == constrainedReference.Cell &&
       endpointGrid.GetComponentId(constrainedOptimized.Cell) == endpointStartComponent,
    "Optimized Bird 9 destination lookup changed the component-constrained endpoint.");
Assert(new AStarPathfinder(endpointGrid).FindPath(endpointEnemy.ColliderCenter, groundedTarget) is null,
    "The regression fixture no longer reproduces the legacy raw-endpoint A* failure.");
endpointNavigation.Step([endpointEnemy], groundedTarget, step);
var endpointAttempt = endpointNavigation.GetDiagnostics([endpointEnemy], groundedTarget)
    .Single().LastPathfindingAttempt!;
Assert(endpointAttempt.NearestTargetCell == new NavigationCell(8, 5) &&
       endpointAttempt.NearestTargetComponentId != endpointAttempt.StartComponentId &&
       endpointTerrain.IntersectsCircle(
           endpointGrid.CellToWorld(endpointAttempt.NearestTargetCell.Value), endpointEnemy.ColliderRadius),
    "The grounded-target regression no longer presents an unconstrained endpoint in the wrong component.");
Assert(!endpointAttempt.RequestedTargetRadiusClear &&
       endpointAttempt.ResolvedDestinationCell is { } replacement &&
       endpointAttempt.ResolvedDestinationWalkable && endpointAttempt.ResolvedDestinationRadiusClear &&
       endpointAttempt.ResolvedDestinationComponentId == endpointAttempt.StartComponentId &&
       endpointGrid.GetComponentId(replacement) == endpointAttempt.StartComponentId,
    "Navigation did not replace the unsuitable grounded-player endpoint with a radius-clear cell in the bird component.");
Assert(endpointAttempt.FailureReason is null && !endpointAttempt.DirectTargetAppended &&
       endpointEnemy.SuccessfulRepathCount == 1 && endpointEnemy.FailedRepathCount == 0 &&
       endpointEnemy.PathIndex < endpointEnemy.Path.Count && endpointEnemy.Path[^1] != groundedTarget,
    "An exhausted stale route was not replaced, or the blocked exact player position was appended.");
Pass("exhausted Bird 9-style route resolves a grounded target to a radius-clear endpoint in its own component");

var exactTarget = new Vector2(3.25f, 0.2f);
var exactEnemy = AtCenter(10, new Vector2(-3.25f, 0.2f), 5);
var exactNavigation = new EnemyNavigationSystem(new AStarPathfinder(movementGrid), emptyTerrain);
exactNavigation.Step([exactEnemy], exactTarget, step);
var exactAttempt = exactNavigation.GetDiagnostics([exactEnemy], exactTarget).Single().LastPathfindingAttempt!;
Assert(exactAttempt.DirectTargetAppended && exactEnemy.Path[^1] == exactTarget,
    "A collision-clear final segment did not append the exact player position.");
for (var index = 0; index < 250; index++) exactNavigation.Step([exactEnemy], exactTarget, step);
Assert(Vector2.Distance(exactEnemy.ColliderCenter, exactTarget) <= EnemyNavigationSystem.EndReachedDistance + 0.01f,
    "A bird did not approach the player closely after direct movement became valid.");
Pass("exact-player append requires a clear final segment and preserves close approach");

var invalidStartEnemy = AtCenter(20, exterior, 5);
var invalidStartNavigation = new EnemyNavigationSystem(authoredFinder, authoredCollision);
invalidStartNavigation.Step([invalidStartEnemy], leftInterior, step);
Assert(Failure(invalidStartNavigation, invalidStartEnemy, leftInterior) ==
       PathfindingFailureReason.InvalidStartWorldPosition,
    "An exterior start was not diagnosed as an invalid start world position.");

var unclearedStartEnemy = AtCenter(21, new Vector2(2f, 0.15f), 5);
endpointNavigation.Step([unclearedStartEnemy], new Vector2(3f, 1f), step);
Assert(Failure(endpointNavigation, unclearedStartEnemy, new Vector2(3f, 1f)) ==
       PathfindingFailureReason.StartLacksRadiusClearance,
    "A traversable start intersecting terrain was not diagnosed as lacking radius clearance.");

var noStartSegment = new TerrainSegment(new Vector2(-1f, 0.6f), new Vector2(1f, 0.6f));
var noStartGrid = new NavigationGrid([noStartSegment], 1, 1, 1f, Vector2.Zero, 0.7f);
var noStartEnemy = AtCenter(22, Vector2.Zero, 5);
var noStartNavigation = new EnemyNavigationSystem(
    new AStarPathfinder(noStartGrid), new TerrainCollisionSystem([noStartSegment]));
noStartNavigation.Step([noStartEnemy], Vector2.Zero, step);
Assert(Failure(noStartNavigation, noStartEnemy, Vector2.Zero) == PathfindingFailureReason.NoStartCell,
    "A clear start without any radius-clear navigation cell was not diagnosed distinctly.");

var distantDivider = new TerrainSegment(new Vector2(0f, -2f), new Vector2(0f, 2f));
var distantGrid = new NavigationGrid([distantDivider], 205, 3, 1f, Vector2.Zero, 0.15f);
var distantTerrain = new TerrainCollisionSystem([distantDivider]);
var distantNavigation = new EnemyNavigationSystem(new AStarPathfinder(distantGrid), distantTerrain);
var distantEnemy = AtCenter(23, new Vector2(-101f, 0f), 5);
var distantTarget = new Vector2(102f, 0f);
distantNavigation.Step([distantEnemy], distantTarget, step);
Assert(Failure(distantNavigation, distantEnemy, distantTarget) ==
       PathfindingFailureReason.NoReachableDestinationCell,
    "A target with no endpoint in the start component inside the projection bound was not diagnosed distinctly.");

Assert(Enum.GetValues<PathfindingFailureReason>().Distinct().Count() == 6 &&
       endpointAttempt.ResolvedDestinationCell is { } validDestination &&
       endpointAttempt.StartCell is { } validStart &&
       new AStarPathfinder(endpointGrid).FindPath(validStart, validDestination) is { Count: > 0 },
    "Endpoint failure reasons overlap, or A* failed despite valid connected endpoints.");
Pass("endpoint diagnostics distinguish invalid, uncleared, missing-start, unreachable-target, and valid-A* outcomes");

var wallTerrain = new TerrainCollisionSystem([new TerrainSegment(new Vector2(0f, -5f), new Vector2(0f, 5f))]);
var blockedNavigation = new EnemyNavigationSystem(new AStarPathfinder(dividedGrid), wallTerrain);
var wallEnemy = AtCenter(0, new Vector2(-1f, 0f), 9);
wallEnemy.Velocity = new Vector2(9f, 0f);
EnemyNavigationDiagnostics? collisionDiagnostic = null;
for (var index = 0; index < 30; index++)
{
    blockedNavigation.Step([wallEnemy], new Vector2(3f, 0f), step);
    var diagnostic = blockedNavigation.GetDiagnostics([wallEnemy], new Vector2(3f, 0f)).Single();
    if (diagnostic.RecentlyCollidedWithTerrain) collisionDiagnostic ??= diagnostic;
}
Assert(collisionDiagnostic is not null &&
       collisionDiagnostic.EnemyId == wallEnemy.Id && collisionDiagnostic.RecentlyCollidedWithTerrain &&
       float.IsFinite(collisionDiagnostic.Position.X) && float.IsFinite(collisionDiagnostic.Velocity.X) &&
       collisionDiagnostic.LastStepDistance >= 0f && collisionDiagnostic.PlayerDistance > 0f &&
       collisionDiagnostic.NavigationComponentId != NavigationGrid.NoComponent &&
       collisionDiagnostic.NearbyEnemyCount == 0,
    "Enemy stall diagnostics did not expose identity, motion, player distance, collision, and crowd state.");
Assert(wallEnemy.ColliderCenter.X <= -wallEnemy.ColliderRadius + 0.01f, "Flying enemy crossed terrain geometry.");
Pass("enemy circle terrain collision");

FindBlockedTargetCase("vertical", [new TerrainSegment(new Vector2(0f, -3f), new Vector2(0f, 3f))]);
FindBlockedTargetCase("corner", [
    new TerrainSegment(new Vector2(0f, -3f), new Vector2(0f, 2f)),
    new TerrainSegment(new Vector2(0f, 2f), new Vector2(3f, 2f)),
]);
FindBlockedTargetCase("diagonal", [new TerrainSegment(new Vector2(-2f, -3f), new Vector2(2f, 3f))]);
Pass("look-ahead steering preserves radius-clear vertical, corner, and diagonal routes");

var cornerCutTerrain = new TerrainCollisionSystem(
    [new TerrainSegment(new Vector2(0f, -2f), new Vector2(0f, 2f))]);
var cornerCutNavigation = new EnemyNavigationSystem(new AStarPathfinder(aroundGrid), cornerCutTerrain);
var cornerCutEnemy = AtCenter(0, new Vector2(-3f, 0f), 5);
for (var index = 0; index < 500; index++)
    cornerCutNavigation.Step([cornerCutEnemy], new Vector2(3f, 0f), step);
Assert(Vector2.Distance(cornerCutEnemy.ColliderCenter, new Vector2(3f, 0f)) < 0.25f,
    "Enemy did not complete a route around an authored wall.");
Pass("enemy follows an authored route around a wall to completion");

var passageSegments = new[]
{
    new TerrainSegment(new Vector2(0f, -5f), new Vector2(0f, -1.25f)),
    new TerrainSegment(new Vector2(0f, 1.25f), new Vector2(0f, 5f)),
};
var passageTerrain = new TerrainCollisionSystem(passageSegments);
var passageGrid = new NavigationGrid(passageSegments, 13, 11, 1f, Vector2.Zero, 0.5f);
var passageNavigation = new EnemyNavigationSystem(new AStarPathfinder(passageGrid), passageTerrain);
var passageEnemies = new[]
{
    AtCenter(0, new Vector2(-5f, -2f), 4),
    AtCenter(1, new Vector2(-5f, 0f), 5),
    AtCenter(2, new Vector2(-5f, 2f), 6),
    AtCenter(3, new Vector2(-4f, -3f), 7),
    AtCenter(4, new Vector2(-4f, 3f), 8),
};
for (var index = 0; index < 750; index++)
{
    passageNavigation.Step(passageEnemies, new Vector2(5f, 0f), step);
    Assert(passageEnemies.All(enemy => !passageTerrain.IntersectsCircle(
            enemy.ColliderCenter, enemy.ColliderRadius, 0.001f)),
        "Multi-bird pursuit penetrated passage terrain.");
}
Assert(passageEnemies.All(enemy => enemy.ColliderCenter.X > 1f),
    "Multi-bird pursuit clustered permanently before a narrow passage.");
Assert(passageNavigation.GetDiagnostics(passageEnemies, new Vector2(5f, 0f))
        .All(item => item.StallDuration < 0.5f),
    "Multi-bird pursuit retained a stalled bird after traversing a narrow passage.");
Pass("multi-bird pursuit traverses a narrow passage without terrain penetration or permanent clustering");

CompareSingleBirdPassage(passageSegments, passageGrid);
CompareMultiBirdPassage(passageSegments, passageGrid);
CompareCrowdedBottleneck();

var separationNavigation = new EnemyNavigationSystem(new AStarPathfinder(openGrid), emptyTerrain);
var clusteredA = AtCenter(0, Vector2.Zero, 2);
var clusteredB = AtCenter(1, Vector2.Zero, 2);
separationNavigation.SeparateEnemies([clusteredA, clusteredB]);
var separatedState = (clusteredA.Position, clusteredB.Position);
var clusteredC = AtCenter(0, Vector2.Zero, 2);
var clusteredD = AtCenter(1, Vector2.Zero, 2);
separationNavigation.SeparateEnemies([clusteredC, clusteredD]);
Assert(separatedState == (clusteredC.Position, clusteredD.Position), "Enemy separation was not deterministic.");
Assert(Vector2.Distance(clusteredA.ColliderCenter, clusteredB.ColliderCenter) >= Enemy.OriginalColliderRadius * 2f - 0.001f,
    "Enemy separation left circles permanently coincident.");
Pass("small deterministic enemy circle separation");

var player = new Player(Vector2.Zero);
Assert(player.Health == 100 && player.IsAlive, "Player did not start alive at 100 health.");
Pass("player starts at 100 health");

var contact = new EnemyCombatSystem();
var touching = AtCenter(0, player.ColliderCenter, 2);
for (var index = 0; index < 50; index++) contact.Step([touching], player, step);
Assert(player.Health == 100, "Contact damage fired before the original >1-second threshold.");
contact.Step([touching], player, step);
Assert(player.Health == 75 && touching.ContactTimer == 0f, "Contact did not deal 25 and reset only its timer after >1 second.");
Pass("contact damage and original >1-second timer rule");

var playerForTimers = new Player(Vector2.Zero);
var timerA = AtCenter(0, playerForTimers.ColliderCenter, 2);
var timerB = AtCenter(1, new Vector2(20f, 20f), 2);
for (var index = 0; index < 51; index++) contact.Step([timerA, timerB], playerForTimers, step);
Assert(timerA.ContactTimer == 0f && timerB.ContactTimer > 1f, "Enemies did not maintain independent continuously advancing timers.");
timerB.Position = playerForTimers.ColliderCenter - timerB.ColliderOffset;
contact.Step([timerA, timerB], playerForTimers, step);
Assert(playerForTimers.Health == 50, "A separately primed enemy timer did not deal contact damage immediately on overlap.");
Pass("separate continuously advancing contact timers");

var mortal = new Player(Vector2.Zero);
for (var index = 0; index < 4; index++) mortal.TakeDamage(25);
Assert(!mortal.IsAlive && mortal.Health == 0, "Player did not die at zero health.");
mortal.Reset(new Vector2(4f, 5f));
Assert(mortal.IsAlive && mortal.Health == 100 && mortal.Position == new Vector2(4f, 5f), "Player reset did not restore health/state.");
Pass("player death and regeneration reset");

var projectileEnemy = AtCenter(0, new Vector2(1f, 0f), 5);
var projectiles = new ProjectileSystem(emptyTerrain);
for (var hit = 1; hit <= 4; hit++)
{
    projectiles.Spawn(new Projectile(Vector2.Zero, Vector2.UnitX, 20f, 25, 0.1f, 0f, 0f));
    projectiles.Step(0.1f, [new ProjectileTarget(projectileEnemy, projectileEnemy.ColliderCenter, projectileEnemy.ColliderRadius)]);
    Assert(projectiles.Projectiles.Count == 0, "Projectile was not consumed on enemy impact.");
    if (hit < 4) Assert(projectileEnemy.IsAlive && projectileEnemy.Health == 100 - hit * 25, "Enemy did not survive one of the first three hits.");
}
Assert(!projectileEnemy.IsAlive && projectileEnemy.Health == 0, "Fourth default bullet did not kill the enemy.");
Pass("projectile damage, first-three survival, and fourth-hit death");

var deadTimer = projectileEnemy.ContactTimer;
var deadPosition = projectileEnemy.Position;
blockedNavigation.Step([projectileEnemy], Vector2.Zero, step);
contact.Step([projectileEnemy], new Player(Vector2.Zero), step);
projectileEnemy.TakeDamage(25);
Assert(projectileEnemy.Position == deadPosition && projectileEnemy.ContactTimer == deadTimer && projectileEnemy.Health == 0,
    "Dead enemy still received damage/navigation/contact updates.");
Pass("dead enemies leave active gameplay systems");

var population = new EnemyPopulation();
population.Reset([AtCenter(0, Vector2.Zero, 2), AtCenter(1, Vector2.One, 3)]);
Assert(!population.AllEnemiesDead, "Population started in all-dead state.");
foreach (var enemy in population.Enemies) enemy.TakeDamage(100);
Assert(population.AllEnemiesDead && population.CreateProjectileTargets().Count == 0, "Final enemy death did not expose AllEnemiesDead/remove targets.");
Pass("AllEnemiesDead after final death");

population.Reset(spawnSelection.CreateEnemies());
Assert(population.LivingCount == 15 && !population.AllEnemiesDead, "Regeneration did not restore 15 living enemies.");
var equivalent = repeatedEnemySpawn.CreateEnemies();
Assert(population.Enemies.Select(State).SequenceEqual(equivalent.Select(State)), "Same deterministic setup produced different enemy state.");
var equivalentPath = new AStarPathfinder(generatedGrid).FindPath(population.Enemies[0].ColliderCenter, Vector2.Zero);
var repeatedPath = new AStarPathfinder(generatedGrid).FindPath(equivalent[0].ColliderCenter, Vector2.Zero);
Assert((equivalentPath is null && repeatedPath is null) || equivalentPath is not null && repeatedPath is not null && equivalentPath.SequenceEqual(repeatedPath),
    "Same deterministic setup produced different navigation state.");
Pass("regeneration count and equivalent deterministic enemy/navigation setup");

DiagnoseObservedSeed(
    "complex",
    CaveGenerationProfiles.Select(CaveGenerationProfile.Complex),
    "1790185384225-daf1e4fe43747dca33459c62f419151");
DiagnoseObservedSeed(
    "extreme",
    CaveGenerationProfiles.Select(CaveGenerationProfile.Extreme),
    "1790185660588-dfad85a8bf5b41cea2b67344d7e93f");
CompareObservedSeed(
    "complex",
    CaveGenerationProfiles.Select(CaveGenerationProfile.Complex),
    "1790185384225-daf1e4fe43747dca33459c62f419151");
CompareObservedSeed(
    "extreme",
    CaveGenerationProfiles.Select(CaveGenerationProfile.Extreme),
    "1790185660588-dfad85a8bf5b41cea2b67344d7e93f");

static void CompareSingleBirdPassage(
    IReadOnlyList<TerrainSegment> segments,
    NavigationGrid grid)
{
    var enabledEnemy = AtCenter(0, new Vector2(-5f, 0f), 6);
    var disabledEnemy = AtCenter(0, new Vector2(-5f, 0f), 6);
    var enabled = new EnemyNavigationSystem(
        new AStarPathfinder(grid), new TerrainCollisionSystem(segments), enableEnemySeparation: true);
    var disabled = new EnemyNavigationSystem(
        new AStarPathfinder(grid), new TerrainCollisionSystem(segments), enableEnemySeparation: false);
    for (var frame = 0; frame < 750; frame++)
    {
        enabled.Step([enabledEnemy], new Vector2(5f, 0f), step);
        disabled.Step([disabledEnemy], new Vector2(5f, 0f), step);
    }
    var enabledDiagnostic = enabled.GetDiagnostics([enabledEnemy], new Vector2(5f, 0f)).Single();
    var disabledDiagnostic = disabled.GetDiagnostics([disabledEnemy], new Vector2(5f, 0f)).Single();
    Assert(enabledEnemy.Position == disabledEnemy.Position &&
           enabledEnemy.Velocity == disabledEnemy.Velocity &&
           enabledDiagnostic.TerrainCollisionCount == disabledDiagnostic.TerrainCollisionCount,
        "Bird interaction toggle changed a deterministic single-bird simulation.");
    Pass($"single-bird interaction A/B is identical (collisions={enabledDiagnostic.TerrainCollisionCount}, " +
         $"distance={enabledDiagnostic.PlayerDistance:F3})");
}

static void CompareMultiBirdPassage(
    IReadOnlyList<TerrainSegment> segments,
    NavigationGrid grid)
{
    var enabled = MeasurePassage(segments, grid, enableEnemySeparation: true);
    var disabled = MeasurePassage(segments, grid, enableEnemySeparation: false);
    Pass($"authored passage A/B: separation on {Format(enabled)}; separation off {Format(disabled)}");
}

static void CompareCrowdedBottleneck()
{
    var segments = new[]
    {
        new TerrainSegment(new Vector2(0f, -6f), new Vector2(0f, -0.6f)),
        new TerrainSegment(new Vector2(0f, 0.6f), new Vector2(0f, 6f)),
    };
    var grid = new NavigationGrid(segments, 21, 13, 1f, Vector2.Zero, 0.5f);
    var enabled = MeasureCrowdedBottleneck(segments, grid, enableEnemySeparation: true);
    var disabled = MeasureCrowdedBottleneck(segments, grid, enableEnemySeparation: false);
    Assert(enabled.Crossed < EnemySpawnSystem.OriginalEnemyCount &&
           enabled.StalledFrames > 0 && enabled.TerrainCollisions > 0,
        "The enabled-separation control no longer reproduces narrow-passage crowding.");
    Assert(disabled.Crossed == EnemySpawnSystem.OriginalEnemyCount &&
           disabled.StalledFrames == 0 && disabled.MaximumStall < 0.5f &&
           disabled.TerrainCollisions == 0 && disabled.MinimumProgress > enabled.MinimumProgress,
        "Overlapping birds did not clear the narrow passage without stalls or terrain collisions.");
    Pass($"crowded one-bird bottleneck A/B: separation on {Format(enabled)}; separation off {Format(disabled)}");
}

static (float MinimumProgress, float TotalTravel, float MaximumStall, int StalledFrames,
    int TerrainCollisions, int Crossed) MeasureCrowdedBottleneck(
        IReadOnlyList<TerrainSegment> segments,
        NavigationGrid grid,
        bool enableEnemySeparation)
{
    var enemies = new List<Enemy>();
    var id = 0;
    for (var x = -8f; x <= -6f; x += 1f)
    for (var y = -2f; y <= 2f; y += 1f)
        enemies.Add(AtCenter(id++, new Vector2(x, y), 6));
    return MeasureScenario(
        enemies, new Vector2(8f, 0f), segments, grid, enableEnemySeparation, frameCount: 1500, crossedX: 1f);
}

static (float MinimumProgress, float TotalTravel, float MaximumStall, int StalledFrames,
    int TerrainCollisions, int Crossed) MeasurePassage(
        IReadOnlyList<TerrainSegment> segments,
        NavigationGrid grid,
        bool enableEnemySeparation)
{
    var enemies = new[]
    {
        AtCenter(0, new Vector2(-5f, -2f), 4),
        AtCenter(1, new Vector2(-5f, 0f), 5),
        AtCenter(2, new Vector2(-5f, 2f), 6),
        AtCenter(3, new Vector2(-4f, -3f), 7),
        AtCenter(4, new Vector2(-4f, 3f), 8),
    };
    return MeasureScenario(
        enemies, new Vector2(5f, 0f), segments, grid, enableEnemySeparation, frameCount: 750, crossedX: 1f);
}

static (float MinimumProgress, float TotalTravel, float MaximumStall, int StalledFrames,
    int TerrainCollisions, int Crossed) MeasureScenario(
        IReadOnlyList<Enemy> enemies,
        Vector2 target,
        IReadOnlyList<TerrainSegment> segments,
        NavigationGrid grid,
        bool enableEnemySeparation,
        int frameCount,
        float crossedX)
{
    var initialDistances = enemies.ToDictionary(enemy => enemy.Id, enemy => Vector2.Distance(enemy.ColliderCenter, target));
    var previous = enemies.ToDictionary(enemy => enemy.Id, enemy => enemy.ColliderCenter);
    var travel = enemies.ToDictionary(enemy => enemy.Id, _ => 0f);
    var maximumStall = 0f;
    var stalledFrames = 0;
    var navigation = new EnemyNavigationSystem(
        new AStarPathfinder(grid), new TerrainCollisionSystem(segments), enableEnemySeparation);
    for (var frame = 0; frame < frameCount; frame++)
    {
        navigation.Step(enemies, target, step);
        foreach (var enemy in enemies)
        {
            travel[enemy.Id] += Vector2.Distance(previous[enemy.Id], enemy.ColliderCenter);
            previous[enemy.Id] = enemy.ColliderCenter;
        }
        foreach (var diagnostic in navigation.GetDiagnostics(enemies, target))
        {
            maximumStall = MathF.Max(maximumStall, diagnostic.StallDuration);
            if (diagnostic.PlayerDistance > EnemyNavigationSystem.StallPlayerDistance &&
                diagnostic.StallDuration >= 0.5f) stalledFrames++;
        }
    }
    var finalDiagnostics = navigation.GetDiagnostics(enemies, target);
    var minimumProgress = finalDiagnostics.Min(
        item => initialDistances[item.EnemyId] - item.PlayerDistance);
    return (minimumProgress, travel.Values.Sum(), maximumStall, stalledFrames,
        finalDiagnostics.Sum(item => item.TerrainCollisionCount),
        enemies.Count(enemy => enemy.ColliderCenter.X > crossedX));
}

static void CompareObservedSeed(
    string profileName,
    CaveGenerationProfileSelection profile,
    string seed)
{
    var enabled = MeasureObservedSeed(profile, seed, enableEnemySeparation: true);
    var disabled = MeasureObservedSeed(profile, seed, enableEnemySeparation: false);
    Assert(disabled.StalledFrames == 0 &&
           disabled.Crossed == enabled.Crossed &&
           disabled.TerrainCollisions < enabled.TerrainCollisions,
        $"Disabling bird interaction did not preserve reachability and reduce terrain collisions for {profileName}.");
    Pass($"supplied {profileName} A/B: separation on {Format(enabled)}; separation off {Format(disabled)}");
}

static (float MinimumProgress, float TotalTravel, float MaximumStall, int StalledFrames,
    int TerrainCollisions, int Crossed) MeasureObservedSeed(
        CaveGenerationProfileSelection profile,
        string seed,
        bool enableEnemySeparation)
{
    var run = new MainScreenRun(seed, profile);
    var enemies = run.Enemies.Enemies;
    var navigation = new EnemyNavigationSystem(
        new AStarPathfinder(run.NavigationGrid), run.TerrainCollision, enableEnemySeparation);
    var initialDistances = enemies.ToDictionary(
        enemy => enemy.Id, enemy => Vector2.Distance(enemy.ColliderCenter, run.Player.Position));
    var previous = enemies.ToDictionary(enemy => enemy.Id, enemy => enemy.ColliderCenter);
    var travel = enemies.ToDictionary(enemy => enemy.Id, _ => 0f);
    var maximumStall = 0f;
    var stalledFrames = 0;
    for (var frame = 0; frame < 1500; frame++)
    {
        navigation.Step(enemies, run.Player.Position, step);
        foreach (var enemy in enemies)
        {
            travel[enemy.Id] += Vector2.Distance(previous[enemy.Id], enemy.ColliderCenter);
            previous[enemy.Id] = enemy.ColliderCenter;
            Assert(!run.TerrainCollision.IntersectsCircle(enemy.ColliderCenter, enemy.ColliderRadius, 0.001f),
                "Interaction A/B simulation penetrated terrain.");
        }
        foreach (var diagnostic in navigation.GetDiagnostics(enemies, run.Player.Position))
        {
            maximumStall = MathF.Max(maximumStall, diagnostic.StallDuration);
            if (diagnostic.PlayerDistance > EnemyNavigationSystem.StallPlayerDistance &&
                diagnostic.StallDuration >= 0.5f) stalledFrames++;
        }
    }
    var finalDiagnostics = navigation.GetDiagnostics(enemies, run.Player.Position);
    var minimumProgress = finalDiagnostics.Min(
        item => initialDistances[item.EnemyId] - item.PlayerDistance);
    return (minimumProgress, travel.Values.Sum(), maximumStall, stalledFrames,
        finalDiagnostics.Sum(item => item.TerrainCollisionCount),
        finalDiagnostics.Count(item => item.PlayerDistance <= EnemyNavigationSystem.StallPlayerDistance));
}

static string Format((float MinimumProgress, float TotalTravel, float MaximumStall, int StalledFrames,
    int TerrainCollisions, int Crossed) result) =>
    $"min-progress={result.MinimumProgress:F3}, travel={result.TotalTravel:F1}, " +
    $"max-stall={result.MaximumStall:F2}s, stalled-frames={result.StalledFrames}, " +
    $"terrain-collisions={result.TerrainCollisions}, reached/crossed={result.Crossed}";

static void FindBlockedTargetCase(string name, IReadOnlyList<TerrainSegment> segments)
{
    var grid = new NavigationGrid(segments, 11, 11, 1f, Vector2.Zero, 0.5f);
    var terrain = new TerrainCollisionSystem(segments);
    var offsets = new[] { Vector2.Zero, new Vector2(0.35f, 0f), new Vector2(-0.35f, 0f),
        new Vector2(0f, 0.35f), new Vector2(0f, -0.35f) };
    for (var startY = 0; startY < grid.Height; startY++)
    for (var startX = 0; startX < grid.Width; startX++)
    foreach (var offset in offsets)
    for (var endY = 0; endY < grid.Height; endY++)
    for (var endX = 0; endX < grid.Width; endX++)
    {
        var startCell = new NavigationCell(startX, startY);
        var endCell = new NavigationCell(endX, endY);
        if (!grid.AreConnected(startCell, endCell)) continue;
        var start = grid.CellToWorld(startCell) + offset;
        var end = grid.CellToWorld(endCell);
        if (terrain.IntersectsCircle(start, Enemy.OriginalColliderRadius, 0f) ||
            Vector2.Distance(start, end) < 3f) continue;
        var enemy = AtCenter(0, start, 5);
        new EnemyNavigationSystem(new AStarPathfinder(grid), terrain).Step([enemy], end, step);
        if (enemy.PathIndex >= enemy.Path.Count ||
            !terrain.TrySweepCircle(enemy.ColliderCenter, enemy.SteeringTarget - enemy.ColliderCenter,
                enemy.ColliderRadius, out var hit) || hit >= 0.9999f) continue;
        throw new InvalidOperationException(
            $"{name} route selected blocked target {enemy.SteeringTarget} from {enemy.ColliderCenter} at hit {hit:F4}.");
    }
}

static void DiagnoseObservedSeed(
    string profileName,
    CaveGenerationProfileSelection profile,
    string seed)
{
    var run = new MainScreenRun(seed, profile);
    var previous = run.Enemies.Enemies.ToDictionary(enemy => enemy.Id, enemy => enemy.ColliderCenter);
    var travel = run.Enemies.Enemies.ToDictionary(enemy => enemy.Id, _ => 0f);
    for (var frame = 0; frame < 1500; frame++)
    {
        run.EnemyNavigation.Step(run.Enemies.Enemies, run.Player.Position, step);
        foreach (var enemy in run.Enemies.Enemies)
        {
            travel[enemy.Id] += Vector2.Distance(previous[enemy.Id], enemy.ColliderCenter);
            previous[enemy.Id] = enemy.ColliderCenter;
        }
    }
    Assert(run.Enemies.Enemies.All(enemy => travel[enemy.Id] > 5f),
        $"Supplied {profileName} seed left a bird without meaningful movement.");
    Assert(run.Enemies.Enemies.All(enemy => !run.TerrainCollision.IntersectsCircle(
            enemy.ColliderCenter, enemy.ColliderRadius, 0.001f)),
        $"Supplied {profileName} seed left a bird penetrating terrain.");
    var stalled = run.EnemyNavigation.GetDiagnostics(run.Enemies.Enemies, run.Player.Position)
        .Where(item => item.PlayerDistance > EnemyNavigationSystem.StallPlayerDistance && item.StallDuration >= 0.5f)
        .ToArray();
    Assert(stalled.Length == 0,
        $"Supplied {profileName} seed retained distant stalled birds: " +
        string.Join("; ", stalled.Select(item =>
            $"{item.EnemyId}@{item.Position} stall={item.StallDuration:F2} distance={item.PlayerDistance:F2} " +
            $"collision={item.RecentlyCollidedWithTerrain} nearby={item.NearbyEnemyCount} waypoint={item.CurrentWaypoint}")));
    Pass($"supplied {profileName} seed moves all birds without penetration or a persistent distant stall");
}

static NavigationGrid Grid(IEnumerable<TerrainSegment> segments, int width = 7, int height = 7, float scanRadius = 0.15f) =>
    new(segments, width, height, 1f, Vector2.Zero, scanRadius);

static Enemy AtCenter(int id, Vector2 center, int speed) => new(id, center - Enemy.OriginalColliderOffset, speed);

static PathfindingFailureReason? Failure(
    EnemyNavigationSystem navigation,
    Enemy enemy,
    Vector2 target) => navigation.GetDiagnostics([enemy], target).Single().LastPathfindingAttempt?.FailureReason;

static IEnumerable<(Enemy First, Enemy Second)> AllPairs(IReadOnlyList<Enemy> enemies)
{
    for (var first = 0; first < enemies.Count; first++)
    for (var second = first + 1; second < enemies.Count; second++)
        yield return (enemies[first], enemies[second]);
}

static bool EnemyIntersectsPlayer(Enemy enemy, Player player, float margin)
{
    var coreOffset = new Vector2(0f, player.Collider.CoreHalfHeight);
    var core = new TerrainSegment(player.ColliderCenter - coreOffset, player.ColliderCenter + coreOffset);
    var required = enemy.ColliderRadius + player.Collider.Radius + margin;
    return TerrainCollisionSystem.DistanceSquaredToSegment(enemy.ColliderCenter, core) < required * required;
}

static (int OptimizedExamined, int OldExamined) VerifyRadiusLookupEquivalence(
    NavigationGrid grid,
    TerrainCollisionSystem terrain,
    string fixture)
{
    var navigation = new EnemyNavigationSystem(new AStarPathfinder(grid), terrain);
    var candidates = new List<(Vector2 World, int? Component)>();
    for (var y = 0; y < grid.Height && candidates.Count < 20; y += 3)
    for (var x = 0; x < grid.Width && candidates.Count < 20; x += 5)
    {
        var cell = new NavigationCell(x, y);
        if (!grid.IsWalkable(cell)) continue;
        var world = grid.CellToWorld(cell) + new Vector2((x % 3 - 1) * 0.19f, (y % 3 - 1) * 0.17f);
        candidates.Add((world, null));
        candidates.Add((world, grid.GetComponentId(cell)));
    }
    Assert(candidates.Count > 0, $"{fixture} provided no representative radius-lookup queries.");

    var optimizedExamined = 0;
    var oldExamined = 0;
    foreach (var query in candidates)
    {
        var optimized = InvokeRadiusLookup(
            navigation, query.World, AStarPathfinder.MaximumNearestNodeDistance,
            query.Component, Enemy.OriginalColliderRadius);
        var reference = ReferenceRadiusLookup(
            grid, terrain, query.World, AStarPathfinder.MaximumNearestNodeDistance,
            query.Component, Enemy.OriginalColliderRadius);
        Assert(optimized.Found == reference.Found && (!optimized.Found || optimized.Cell == reference.Cell),
            $"{fixture} changed endpoint selection for {query.World} in component {query.Component?.ToString() ?? "any"}: " +
            $"optimized={optimized.Found}/{optimized.Cell}, reference={reference.Found}/{reference.Cell}.");
        if (optimized.Found)
        {
            Assert(grid.IsWalkable(optimized.Cell) &&
                   (!query.Component.HasValue || grid.GetComponentId(optimized.Cell) == query.Component.Value) &&
                   !terrain.IntersectsCircle(grid.CellToWorld(optimized.Cell), Enemy.OriginalColliderRadius),
                $"{fixture} selected an endpoint without walkability, component, or collider-radius clearance.");
        }
        optimizedExamined += optimized.Examined;
        oldExamined += reference.Examined;
    }
    return (optimizedExamined, oldExamined);
}

static (bool Found, NavigationCell Cell, int Examined) InvokeRadiusLookup(
    EnemyNavigationSystem navigation,
    Vector2 world,
    float maximumDistance,
    int? componentId,
    float radius)
{
    var method = typeof(EnemyNavigationSystem).GetMethod(
        "TryFindNearestRadiusClearCellForTesting", BindingFlags.Instance | BindingFlags.NonPublic)!;
    object?[] arguments = [world, maximumDistance, componentId, radius, default(NavigationCell), 0];
    var found = (bool)method.Invoke(navigation, arguments)!;
    return (found, (NavigationCell)arguments[4]!, (int)arguments[5]!);
}

static (bool Found, NavigationCell Cell, int Examined) ReferenceRadiusLookup(
    NavigationGrid grid,
    TerrainCollisionSystem terrain,
    Vector2 world,
    float maximumDistance,
    int? componentId,
    float radius)
{
    var maximumSquared = maximumDistance * maximumDistance;
    var bestDistance = float.PositiveInfinity;
    var result = default(NavigationCell);
    var found = false;
    var examined = 0;
    for (var y = 0; y < grid.Height; y++)
    for (var x = 0; x < grid.Width; x++)
    {
        examined++;
        var cell = new NavigationCell(x, y);
        if (!grid.IsWalkable(cell) || componentId.HasValue && grid.GetComponentId(cell) != componentId.Value)
            continue;
        var cellWorld = grid.CellToWorld(cell);
        var distance = Vector2.DistanceSquared(world, cellWorld);
        if (distance > maximumSquared || distance >= bestDistance || terrain.IntersectsCircle(cellWorld, radius))
            continue;
        bestDistance = distance;
        result = cell;
        found = true;
    }
    return (found, result, examined);
}

static (TerrainOccupancy Occupancy, TerrainCollisionSystem Collision, NavigationGrid Navigation,
    PlayerSpawnResult PlayerSpawn) BuildSpawnContext(CaveMap map, PlayerSpawnOptions playerOptions)
{
    var geometry = new CaveGeometryBuilder().Build(map);
    var occupancy = new TerrainOccupancy(geometry);
    var collision = new TerrainCollisionSystem(occupancy);
    var navigation = new NavigationGrid(occupancy);
    var topology = new CaveTopologyAnalyzer().Analyze(map, geometry, occupancy, navigation);
    var playerSpawn = PlayerSpawnSystem.Select(
        topology, geometry, occupancy, collision, navigation, PlayerCollider.Original, playerOptions);
    return (occupancy, collision, navigation, playerSpawn);
}

static CaveMap CreateSmallCapacityMap()
{
    var tiles = new CaveTile[8, 8];
    for (var x = 0; x < tiles.GetLength(0); x++)
    for (var y = 0; y < tiles.GetLength(1); y++)
        tiles[x, y] = x is >= 2 and <= 5 && y is >= 2 and <= 5 ? CaveTile.Open : CaveTile.Wall;
    return new CaveMap(tiles);
}

static CaveMap CreateTwoChamberMap()
{
    var tiles = new CaveTile[11, 9];
    for (var x = 0; x < tiles.GetLength(0); x++)
    for (var y = 0; y < tiles.GetLength(1); y++)
        tiles[x, y] = CaveTile.Wall;
    for (var y = 1; y <= 7; y++)
    {
        for (var x = 1; x <= 3; x++) tiles[x, y] = CaveTile.Open;
        for (var x = 7; x <= 9; x++) tiles[x, y] = CaveTile.Open;
    }
    return new CaveMap(tiles);
}

static (Vector2 Position, int Speed, int Health, bool Alive) State(Enemy enemy) =>
    (enemy.Position, enemy.MaximumSpeed, enemy.Health, enemy.IsAlive);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Pass(string message) => Console.WriteLine($"PASS: {message}");
