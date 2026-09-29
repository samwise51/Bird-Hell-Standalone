using System.Numerics;
using BirdHell.Entities;
using BirdHell.World;

namespace BirdHell.Systems;

public enum PathfindingFailureReason
{
    InvalidStartWorldPosition,
    StartLacksRadiusClearance,
    NoStartCell,
    NoReachableDestinationCell,
    ResolvedDestinationLacksRadiusClearance,
    AStarFailedWithValidEndpoints,
}

public sealed record PathfindingAttemptDiagnostics(
    Vector2 RequestedStartWorldPosition,
    Vector2 RequestedTargetWorldPosition,
    NavigationCell? StartCell,
    NavigationCell? NearestTargetCell,
    NavigationCell? ResolvedDestinationCell,
    bool StartTraversable,
    bool StartRadiusClear,
    bool RequestedTargetTraversable,
    bool RequestedTargetRadiusClear,
    bool ResolvedDestinationWalkable,
    bool ResolvedDestinationRadiusClear,
    int StartComponentId,
    int NearestTargetComponentId,
    int ResolvedDestinationComponentId,
    bool DirectTargetAppended,
    PathfindingFailureReason? FailureReason);

public sealed record EnemyNavigationDiagnostics(
    int EnemyId,
    Vector2 Position,
    Vector2 Velocity,
    Vector2? CurrentWaypoint,
    float LastStepDistance,
    float PlayerDistance,
    float PlayerDistanceDelta,
    float StallDuration,
    int NavigationComponentId,
    bool RecentlyCollidedWithTerrain,
    int TerrainCollisionCount,
    int NearbyEnemyCount,
    Vector2 IntendedMovement,
    Vector2 ActualMovement,
    bool CollidedWithTerrainThisStep,
    PathfindingAttemptDiagnostics? LastPathfindingAttempt);

public sealed class EnemyNavigationSystem
{
    public const float MaximumAcceleration = 10f;
    public const float SlowdownDistance = 0.6f;
    public const float EndReachedDistance = 0.2f;
    public const float RepathInterval = 0.5f;
    public const float PathLookAheadDistance = 2f;
    public const float StallMovementThreshold = 0.005f;
    public const float StallPlayerDistance = 3f;
    public const float RecentCollisionDuration = 0.25f;
    public const bool DefaultEnemySeparationEnabled = false;
    private readonly AStarPathfinder _pathfinder;
    private readonly TerrainCollisionSystem _terrain;
    private readonly bool _enableEnemySeparation;
    private readonly Dictionary<int, MotionState> _motion = [];

    public EnemyNavigationSystem(
        AStarPathfinder pathfinder,
        TerrainCollisionSystem terrain,
        bool enableEnemySeparation = DefaultEnemySeparationEnabled)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _enableEnemySeparation = enableEnemySeparation;
    }

    public bool EnemySeparationEnabled => _enableEnemySeparation;

    public void Step(IReadOnlyList<Enemy> enemies, Vector2 playerPosition, float deltaTime)
    {
        if (deltaTime <= 0f || !float.IsFinite(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        var ordered = enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.Id).ToArray();
        var previousCenters = ordered.Select(enemy => enemy.ColliderCenter).ToArray();
        var previousPlayerDistances = ordered.Select(
            enemy => Vector2.Distance(enemy.ColliderCenter, playerPosition)).ToArray();
        var collided = new bool[ordered.Length];
        for (var enemyIndex = 0; enemyIndex < ordered.Length; enemyIndex++)
        {
            var enemy = ordered[enemyIndex];
            enemy.AdvanceFeedback(deltaTime);
            enemy.RepathTimer += deltaTime;
            if (enemy.RepathTimer >= RepathInterval)
            {
                enemy.RepathTimer %= RepathInterval;
                var attempt = FindPath(enemy.ColliderCenter, playerPosition, enemy.ColliderRadius);
                GetMotionState(enemy.Id).LastPathfindingAttempt = attempt.Diagnostics;
                var newPath = attempt.Path;
                if (newPath is { Count: > 0 })
                {
                    enemy.Path = newPath;
                    enemy.PathIndex = 0;
                    enemy.SuccessfulRepathCount++;
                }
                else enemy.FailedRepathCount++;
            }

            AdvanceWaypoint(enemy);
            var desiredVelocity = GetDesiredVelocity(enemy);
            enemy.Velocity = MoveTowards(enemy.Velocity, desiredVelocity, MaximumAcceleration * deltaTime);
            if (enemy.Velocity.LengthSquared() > enemy.MaximumSpeed * enemy.MaximumSpeed)
                enemy.Velocity = Vector2.Normalize(enemy.Velocity) * enemy.MaximumSpeed;
            var center = enemy.ColliderCenter;
            var velocity = enemy.Velocity;
            GetMotionState(enemy.Id).IntendedMovement = velocity * deltaTime;
            collided[enemyIndex] = _terrain.MoveCircle(
                ref center, ref velocity, velocity * deltaTime, enemy.ColliderRadius);
            enemy.Position = center - enemy.ColliderOffset;
            enemy.Velocity = velocity;
            enemy.UpdateFacingFromHorizontalMovement(enemy.Velocity.X);
        }
        if (_enableEnemySeparation) SeparateEnemies(enemies);
        for (var enemyIndex = 0; enemyIndex < ordered.Length; enemyIndex++)
        {
            var enemy = ordered[enemyIndex];
            var state = GetMotionState(enemy.Id);
            var distance = Vector2.Distance(enemy.ColliderCenter, playerPosition);
            state.LastStepDistance = Vector2.Distance(previousCenters[enemyIndex], enemy.ColliderCenter);
            state.ActualMovement = enemy.ColliderCenter - previousCenters[enemyIndex];
            state.CollidedThisStep = collided[enemyIndex];
            state.PlayerDistanceDelta = previousPlayerDistances[enemyIndex] - distance;
            state.StallDuration = distance > StallPlayerDistance &&
                                  enemy.PathIndex < enemy.Path.Count &&
                                  state.LastStepDistance < StallMovementThreshold
                ? state.StallDuration + deltaTime
                : 0f;
            state.RecentCollisionTime = collided[enemyIndex]
                ? RecentCollisionDuration
                : MathF.Max(0f, state.RecentCollisionTime - deltaTime);
            if (collided[enemyIndex]) state.TerrainCollisionCount++;
        }
    }

    public IReadOnlyList<EnemyNavigationDiagnostics> GetDiagnostics(
        IReadOnlyList<Enemy> enemies,
        Vector2 playerPosition)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        var alive = enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.Id).ToArray();
        return alive.Select(enemy =>
        {
            var state = GetMotionState(enemy.Id);
            var componentId = _pathfinder.Grid.TryGetComponentForPosition(
                enemy.ColliderCenter, _pathfinder.Grid.NodeSize * 2f, out var component, out _)
                ? component
                : NavigationGrid.NoComponent;
            var nearby = alive.Count(other => other.Id != enemy.Id &&
                Vector2.Distance(other.ColliderCenter, enemy.ColliderCenter) <= enemy.ColliderRadius * 3f);
            return new EnemyNavigationDiagnostics(
                enemy.Id,
                enemy.Position,
                enemy.Velocity,
                enemy.PathIndex < enemy.Path.Count ? enemy.SteeringTarget : null,
                state.LastStepDistance,
                Vector2.Distance(enemy.ColliderCenter, playerPosition),
                state.PlayerDistanceDelta,
                state.StallDuration,
                componentId,
                state.RecentCollisionTime > 0f,
                state.TerrainCollisionCount,
                nearby,
                state.IntendedMovement,
                state.ActualMovement,
                state.CollidedThisStep,
                state.LastPathfindingAttempt);
        }).ToArray();
    }

    private PathfindingAttempt FindPath(Vector2 start, Vector2 target, float radius)
    {
        var grid = _pathfinder.Grid;
        var startTraversable = grid.IsWorldPositionInTraversableSpace(start);
        var startClear = startTraversable && !_terrain.IntersectsCircle(start, radius);
        var targetTraversable = grid.IsWorldPositionInTraversableSpace(target);
        var targetClear = targetTraversable && !_terrain.IntersectsCircle(target, radius);
        var nearestTargetFound = grid.TryFindNearestWalkable(
            target, AStarPathfinder.MaximumNearestNodeDistance, out var nearestTargetCell);
        var nearestTargetComponent = nearestTargetFound
            ? grid.GetComponentId(nearestTargetCell)
            : NavigationGrid.NoComponent;

        NavigationCell? startCell = null;
        NavigationCell? resolvedCell = null;
        var startComponent = NavigationGrid.NoComponent;
        var resolvedComponent = NavigationGrid.NoComponent;
        var resolvedWalkable = false;
        var resolvedClear = false;
        var directTargetAppended = false;
        PathfindingFailureReason? failure = null;
        IReadOnlyList<Vector2>? path = null;

        if (!startTraversable) failure = PathfindingFailureReason.InvalidStartWorldPosition;
        else if (!startClear) failure = PathfindingFailureReason.StartLacksRadiusClearance;
        else if (!TryFindNearestRadiusClearCell(
                     start, AStarPathfinder.MaximumNearestNodeDistance, null, radius, out var foundStartCell))
            failure = PathfindingFailureReason.NoStartCell;
        else
        {
            startCell = foundStartCell;
            startComponent = grid.GetComponentId(foundStartCell);
            if (!TryFindNearestRadiusClearCell(
                    target, AStarPathfinder.MaximumNearestNodeDistance, startComponent, radius, out var foundDestination))
                failure = PathfindingFailureReason.NoReachableDestinationCell;
            else
            {
                resolvedCell = foundDestination;
                resolvedComponent = grid.GetComponentId(foundDestination);
                resolvedWalkable = grid.IsWalkable(foundDestination);
                var resolvedWorld = grid.CellToWorld(foundDestination);
                resolvedClear = !_terrain.IntersectsCircle(resolvedWorld, radius);
                if (!resolvedClear) failure = PathfindingFailureReason.ResolvedDestinationLacksRadiusClearance;
                else
                {
                    path = _pathfinder.FindPath(foundStartCell, foundDestination);
                    if (path is null) failure = PathfindingFailureReason.AStarFailedWithValidEndpoints;
                    else if (targetClear && HasClearRoute(resolvedWorld, target, radius) &&
                             Vector2.DistanceSquared(path[^1], target) > 0.000001f)
                    {
                        path = path.Append(target).ToArray();
                        directTargetAppended = true;
                    }
                }
            }
        }

        var diagnostics = new PathfindingAttemptDiagnostics(
            start,
            target,
            startCell,
            nearestTargetFound ? nearestTargetCell : null,
            resolvedCell,
            startTraversable,
            startClear,
            targetTraversable,
            targetClear,
            resolvedWalkable,
            resolvedClear,
            startComponent,
            nearestTargetComponent,
            resolvedComponent,
            directTargetAppended,
            failure);
        return new PathfindingAttempt(path, diagnostics);
    }

    private bool TryFindNearestRadiusClearCell(
        Vector2 world,
        float maximumDistance,
        int? componentId,
        float radius,
        out NavigationCell result) =>
        TryFindNearestRadiusClearCellCore(
            world, maximumDistance, componentId, radius, out result, out _);

    // Kept separate from the production signature so endpoint-search operation counts remain
    // testable without coupling normal pathfinding or stall profiling to measurement state.
    private bool TryFindNearestRadiusClearCellForTesting(
        Vector2 world,
        float maximumDistance,
        int? componentId,
        float radius,
        out NavigationCell result,
        out int examinedCells) =>
        TryFindNearestRadiusClearCellCore(
            world, maximumDistance, componentId, radius, out result, out examinedCells);

    private bool TryFindNearestRadiusClearCellCore(
        Vector2 world,
        float maximumDistance,
        int? componentId,
        float radius,
        out NavigationCell result,
        out int examinedCells)
    {
        var grid = _pathfinder.Grid;
        var maximumSquared = maximumDistance * maximumDistance;
        var bestDistance = float.PositiveInfinity;
        var bestIndex = int.MaxValue;
        var selectedCell = default(NavigationCell);
        var examined = 0;
        var found = false;

        var origin = grid.WorldToCell(world);
        var originWorld = grid.CellToWorld(origin);
        var nearestXDistance = world.X - originWorld.X;
        var nearestYDistance = world.Y - originWorld.Y;
        var nearestXSquared = nearestXDistance * nearestXDistance;
        var nearestYSquared = nearestYDistance * nearestYDistance;
        var maximumRing = Math.Max(
            Math.Max(origin.X, grid.Width - 1 - origin.X),
            Math.Max(origin.Y, grid.Height - 1 - origin.Y));

        for (var ring = 0; ring <= maximumRing; ring++)
        {
            var minimumX = Math.Max(0, origin.X - ring);
            var maximumX = Math.Min(grid.Width - 1, origin.X + ring);
            var minimumY = Math.Max(0, origin.Y - ring);
            var maximumY = Math.Min(grid.Height - 1, origin.Y + ring);

            var bottomY = origin.Y - ring;
            var topY = origin.Y + ring;
            if (bottomY >= 0)
                for (var x = minimumX; x <= maximumX; x++)
                    Consider(new NavigationCell(x, bottomY));
            if (topY < grid.Height && topY != bottomY)
                for (var x = minimumX; x <= maximumX; x++)
                    Consider(new NavigationCell(x, topY));

            var leftX = origin.X - ring;
            var rightX = origin.X + ring;
            var firstInteriorY = Math.Max(0, origin.Y - ring + 1);
            var lastInteriorY = Math.Min(grid.Height - 1, origin.Y + ring - 1);
            if (leftX >= 0)
            {
                for (var y = firstInteriorY; y <= lastInteriorY; y++)
                    Consider(new NavigationCell(leftX, y));
            }
            if (rightX < grid.Width && rightX != leftX)
            {
                for (var y = firstInteriorY; y <= lastInteriorY; y++)
                    Consider(new NavigationCell(rightX, y));
            }

            var nearestUnvisitedDistance = float.PositiveInfinity;
            if (minimumX > 0)
                nearestUnvisitedDistance = MathF.Min(
                    nearestUnvisitedDistance,
                    AxisDistanceSquared(minimumX - 1, true) + nearestYSquared);
            if (maximumX < grid.Width - 1)
                nearestUnvisitedDistance = MathF.Min(
                    nearestUnvisitedDistance,
                    AxisDistanceSquared(maximumX + 1, true) + nearestYSquared);
            if (minimumY > 0)
                nearestUnvisitedDistance = MathF.Min(
                    nearestUnvisitedDistance,
                    AxisDistanceSquared(minimumY - 1, false) + nearestXSquared);
            if (maximumY < grid.Height - 1)
                nearestUnvisitedDistance = MathF.Min(
                    nearestUnvisitedDistance,
                    AxisDistanceSquared(maximumY + 1, false) + nearestXSquared);

            // Strict comparison preserves row-major tie-breaking by continuing through every
            // unvisited cell that could be exactly as close as the current best cell.
            if (found && bestDistance < nearestUnvisitedDistance) break;
            if (nearestUnvisitedDistance > maximumSquared) break;
        }
        result = selectedCell;
        examinedCells = examined;
        return found;

        void Consider(NavigationCell cell)
        {
            examined++;
            var cellWorld = grid.CellToWorld(cell);
            var distance = Vector2.DistanceSquared(world, cellWorld);
            if (distance > maximumSquared ||
                !grid.IsWalkable(cell) ||
                componentId.HasValue && grid.GetComponentId(cell) != componentId.Value ||
                _terrain.IntersectsCircle(cellWorld, radius))
                return;

            var index = cell.ToIndex(grid.Width);
            if (distance > bestDistance || distance == bestDistance && index >= bestIndex) return;
            bestDistance = distance;
            bestIndex = index;
            selectedCell = cell;
            found = true;
        }

        float AxisDistanceSquared(int index, bool horizontal)
        {
            var coordinate = horizontal
                ? grid.Minimum.X + (index + 0.5f) * grid.NodeSize
                : grid.Minimum.Y + (index + 0.5f) * grid.NodeSize;
            var delta = (horizontal ? world.X : world.Y) - coordinate;
            return delta * delta;
        }
    }

    public void SeparateEnemies(IReadOnlyList<Enemy> enemies)
    {
        var ordered = enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.Id).ToArray();
        for (var pass = 0; pass < 2; pass++)
        for (var firstIndex = 0; firstIndex < ordered.Length; firstIndex++)
        for (var secondIndex = firstIndex + 1; secondIndex < ordered.Length; secondIndex++)
        {
            var first = ordered[firstIndex];
            var second = ordered[secondIndex];
            var delta = second.ColliderCenter - first.ColliderCenter;
            var minimum = first.ColliderRadius + second.ColliderRadius;
            var distance = delta.Length();
            if (distance >= minimum) continue;
            var normal = distance > 0.000001f
                ? delta / distance
                : Vector2.Normalize(new Vector2(((first.Id + second.Id) & 1) == 0 ? 1f : -1f, 0.5f));
            var correction = normal * ((minimum - distance) * 0.5f + 0.0001f);
            TrySeparate(first, -correction);
            TrySeparate(second, correction);
        }
    }

    private void TrySeparate(Enemy enemy, Vector2 correction)
    {
        var candidateCenter = enemy.ColliderCenter + correction;
        if (!_terrain.IntersectsCircle(candidateCenter, enemy.ColliderRadius)) enemy.Position += correction;
    }

    private void AdvanceWaypoint(Enemy enemy)
    {
        while (enemy.PathIndex < enemy.Path.Count - 1 &&
               Vector2.Distance(enemy.ColliderCenter, enemy.Path[enemy.PathIndex]) <= PathLookAheadDistance &&
               HasClearRoute(enemy.ColliderCenter, enemy.Path[enemy.PathIndex + 1], enemy.ColliderRadius))
            enemy.PathIndex++;
        if (enemy.PathIndex < enemy.Path.Count) enemy.SteeringTarget = enemy.Path[enemy.PathIndex];
    }

    private bool HasClearRoute(Vector2 start, Vector2 end, float radius)
    {
        var displacement = end - start;
        return displacement.LengthSquared() <= 0.000001f ||
               !_terrain.TrySweepCircle(start, displacement, radius, out var hitFraction) ||
               hitFraction >= 0.9999f;
    }

    private MotionState GetMotionState(int enemyId)
    {
        if (_motion.TryGetValue(enemyId, out var state)) return state;
        state = new MotionState();
        _motion.Add(enemyId, state);
        return state;
    }

    private static Vector2 GetDesiredVelocity(Enemy enemy)
    {
        if (enemy.PathIndex >= enemy.Path.Count) return Vector2.Zero;
        var offset = enemy.SteeringTarget - enemy.ColliderCenter;
        var distance = offset.Length();
        if (enemy.PathIndex == enemy.Path.Count - 1 && distance <= EndReachedDistance) return Vector2.Zero;
        if (distance <= 0.000001f) return Vector2.Zero;
        var speed = (float)enemy.MaximumSpeed;
        if (enemy.PathIndex == enemy.Path.Count - 1 && distance < SlowdownDistance)
            speed *= distance / SlowdownDistance;
        return offset / distance * speed;
    }

    private static Vector2 MoveTowards(Vector2 current, Vector2 target, float maximumDelta)
    {
        var delta = target - current;
        var length = delta.Length();
        return length <= maximumDelta || length <= 0.000001f ? target : current + delta / length * maximumDelta;
    }

    private sealed class MotionState
    {
        public float LastStepDistance { get; set; }
        public float PlayerDistanceDelta { get; set; }
        public float StallDuration { get; set; }
        public float RecentCollisionTime { get; set; }
        public int TerrainCollisionCount { get; set; }
        public Vector2 IntendedMovement { get; set; }
        public Vector2 ActualMovement { get; set; }
        public bool CollidedThisStep { get; set; }
        public PathfindingAttemptDiagnostics? LastPathfindingAttempt { get; set; }
    }

    private sealed record PathfindingAttempt(
        IReadOnlyList<Vector2>? Path,
        PathfindingAttemptDiagnostics Diagnostics);
}
