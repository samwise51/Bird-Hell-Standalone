using System.Numerics;
using BirdHell.Entities;
using BirdHell.World.Geometry;

namespace BirdHell.Systems;

public interface IDamageable
{
    bool CanTakeDamage => true;
    DamageResult TakeDamage(int damage);
    DamageResult TakeHit(HitContext hit) => TakeDamage(hit.Damage);
}

public readonly record struct DamageResult(bool Applied, bool Killed);
public readonly record struct ProjectileTarget(IDamageable Damageable, Vector2 Center, float Radius);
public readonly record struct ProjectileStepResult(
    int DamageEvents,
    int DeathEvents,
    IReadOnlyList<AcceptedHit>? AcceptedHits = null)
{
    public IReadOnlyList<AcceptedHit> Hits => AcceptedHits ?? Array.Empty<AcceptedHit>();
}

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
        List<AcceptedHit>? acceptedHits = null;

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
                var impactPosition = projectile.ColliderCenter + displacement * targetFraction;
                var hit = new HitContext(
                    projectile.Damage,
                    projectile.ColliderCenter,
                    impactPosition,
                    projectile.Direction,
                    HitReactionSettings.ProjectileEnemyKnockbackSpeed,
                    HitSourceCategory.Projectile);
                var damage = targetHit.TakeHit(hit);
                if (damage.Applied)
                {
                    damageEvents++;
                    (acceptedHits ??= []).Add(new AcceptedHit(
                        HitVictimCategory.Enemy,
                        hit.SourcePosition,
                        hit.ImpactPosition,
                        hit.NormalizedForceDirection,
                        hit.SourceCategory));
                }
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
        return new ProjectileStepResult(damageEvents, deathEvents, acceptedHits);
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
