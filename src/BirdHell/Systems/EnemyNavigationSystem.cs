using System.Numerics;
using BirdHell.Entities;
using BirdHell.World;

namespace BirdHell.Systems;

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
    int NearbyEnemyCount);

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
                var newPath = _pathfinder.FindPath(enemy.ColliderCenter, playerPosition);
                if (newPath is { Count: > 0 })
                {
                    enemy.Path = newPath;
                    enemy.PathIndex = 0;
                    enemy.SuccessfulRepathCount++;
                }
            }

            AdvanceWaypoint(enemy);
            var desiredVelocity = GetDesiredVelocity(enemy);
            enemy.Velocity = MoveTowards(enemy.Velocity, desiredVelocity, MaximumAcceleration * deltaTime);
            if (enemy.Velocity.LengthSquared() > enemy.MaximumSpeed * enemy.MaximumSpeed)
                enemy.Velocity = Vector2.Normalize(enemy.Velocity) * enemy.MaximumSpeed;
            var center = enemy.ColliderCenter;
            var velocity = enemy.Velocity;
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
                nearby);
        }).ToArray();
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
    }
}
