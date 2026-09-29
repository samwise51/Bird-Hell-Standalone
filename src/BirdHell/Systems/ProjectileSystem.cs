using System.Numerics;
using BirdHell.Entities;
using BirdHell.World.Geometry;

namespace BirdHell.Systems;

public interface IDamageable
{
    bool CanTakeDamage => true;
    DamageResult TakeDamage(int damage);
}

public readonly record struct DamageResult(bool Applied, bool Killed);
public readonly record struct ProjectileTarget(IDamageable Damageable, Vector2 Center, float Radius);
public readonly record struct ProjectileStepResult(int DamageEvents, int DeathEvents);

public sealed class ProjectileSystem
{
    private readonly TerrainCollisionSystem _terrain;
    private readonly List<Projectile> _projectiles = [];

    public ProjectileSystem(CaveGeometry geometry) : this(new TerrainCollisionSystem(geometry))
    {
    }

    public ProjectileSystem(TerrainCollisionSystem terrain)
    {
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
    }

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    public void Spawn(Projectile projectile)
    {
        ArgumentNullException.ThrowIfNull(projectile);
        _projectiles.Add(projectile);
    }

    public void Clear() => _projectiles.Clear();

    public ProjectileStepResult Step(float deltaTime, IEnumerable<ProjectileTarget>? targets = null)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f || deltaTime > 1f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        var targetArray = targets?.ToArray() ?? [];
        var damageEvents = 0;
        var deathEvents = 0;

        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var projectile = _projectiles[index];
            var displacement = projectile.Velocity * deltaTime;
            var terrainHit = _terrain.TrySweepCircle(
                projectile.ColliderCenter,
                displacement,
                projectile.ColliderRadius,
                out var terrainFraction);
            var targetHit = FindFirstTargetHit(projectile, displacement, targetArray, out var targetFraction);

            if (targetHit is not null && (!terrainHit || targetFraction <= terrainFraction))
            {
                var damage = targetHit.TakeDamage(projectile.Damage);
                if (damage.Applied) damageEvents++;
                if (damage.Killed) deathEvents++;
                _projectiles.RemoveAt(index);
                continue;
            }
            if (terrainHit)
            {
                _projectiles.RemoveAt(index);
                continue;
            }

            projectile.Position += displacement;
            projectile.Age += deltaTime;
            if (projectile.MaximumLifetime is { } lifetime && projectile.Age >= lifetime)
                _projectiles.RemoveAt(index);
        }
        return new ProjectileStepResult(damageEvents, deathEvents);
    }

    private static IDamageable? FindFirstTargetHit(
        Projectile projectile,
        Vector2 displacement,
        IReadOnlyList<ProjectileTarget> targets,
        out float hitFraction)
    {
        hitFraction = float.PositiveInfinity;
        IDamageable? result = null;
        foreach (var target in targets)
        {
            if (target.Damageable is null || !target.Damageable.CanTakeDamage || target.Radius < 0f) continue;
            if (!TerrainCollisionSystem.TrySweepPointAgainstCircle(
                    projectile.ColliderCenter,
                    displacement,
                    target.Center,
                    projectile.ColliderRadius + target.Radius,
                    out var candidate) || candidate >= hitFraction)
                continue;
            hitFraction = candidate;
            result = target.Damageable;
        }
        return result;
    }
}
