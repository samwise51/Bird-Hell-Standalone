using System.Numerics;
using BirdHell.Entities;

namespace BirdHell.Systems;

public sealed class EnemyNavigationSystem
{
    public const float MaximumAcceleration = 10f;
    public const float SlowdownDistance = 0.6f;
    public const float EndReachedDistance = 0.2f;
    public const float RepathInterval = 0.5f;
    public const float PathLookAheadDistance = 2f;
    private readonly AStarPathfinder _pathfinder;
    private readonly TerrainCollisionSystem _terrain;

    public EnemyNavigationSystem(AStarPathfinder pathfinder, TerrainCollisionSystem terrain)
    {
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
    }

    public void Step(IReadOnlyList<Enemy> enemies, Vector2 playerPosition, float deltaTime)
    {
        if (deltaTime <= 0f || !float.IsFinite(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        foreach (var enemy in enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.Id))
        {
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
            if (desiredVelocity.X >= 0.01f) enemy.FacingDirection = 1;
            else if (desiredVelocity.X <= -0.01f) enemy.FacingDirection = -1;

            var center = enemy.ColliderCenter;
            var velocity = enemy.Velocity;
            _terrain.MoveCircle(ref center, ref velocity, velocity * deltaTime, enemy.ColliderRadius);
            enemy.Position = center - enemy.ColliderOffset;
            enemy.Velocity = velocity;
        }
        SeparateEnemies(enemies);
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

    private static void AdvanceWaypoint(Enemy enemy)
    {
        while (enemy.PathIndex < enemy.Path.Count - 1 &&
               Vector2.Distance(enemy.ColliderCenter, enemy.Path[enemy.PathIndex]) <= PathLookAheadDistance)
            enemy.PathIndex++;
        if (enemy.PathIndex < enemy.Path.Count) enemy.SteeringTarget = enemy.Path[enemy.PathIndex];
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
}
