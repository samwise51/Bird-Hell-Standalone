using System.Numerics;
using BirdHell.Entities;

namespace BirdHell.Systems;

public sealed class EnemyCombatSystem
{
    public const int ContactDamage = 25;
    public const float ContactDelay = 1f;

    public EnemyCombatStepResult Step(IReadOnlyList<Enemy> enemies, Player player, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        ArgumentNullException.ThrowIfNull(player);
        if (deltaTime <= 0f || !float.IsFinite(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        var damageEvents = 0;
        var playerDied = false;
        foreach (var enemy in enemies.Where(enemy => enemy.IsAlive).OrderBy(enemy => enemy.Id))
        {
            enemy.ContactTimer += deltaTime;
            if (!player.IsAlive || enemy.ContactTimer <= ContactDelay || !Overlaps(enemy, player)) continue;
            var damage = player.TakeDamage(ContactDamage);
            if (damage.Applied) damageEvents++;
            playerDied |= damage.Killed;
            enemy.ContactTimer = 0f;
        }
        return new EnemyCombatStepResult(damageEvents, playerDied);
    }

    public static bool Overlaps(Enemy enemy, Player player)
    {
        var center = player.ColliderCenter;
        var first = center - Vector2.UnitY * player.Collider.CoreHalfHeight;
        var second = center + Vector2.UnitY * player.Collider.CoreHalfHeight;
        var edge = second - first;
        var t = edge.LengthSquared() <= 0.000001f
            ? 0f
            : Math.Clamp(Vector2.Dot(enemy.ColliderCenter - first, edge) / edge.LengthSquared(), 0f, 1f);
        var closest = first + edge * t;
        var radius = player.Collider.Radius + enemy.ColliderRadius;
        return Vector2.DistanceSquared(enemy.ColliderCenter, closest) <= radius * radius;
    }
}

public readonly record struct EnemyCombatStepResult(int PlayerDamageEvents, bool PlayerDied);
