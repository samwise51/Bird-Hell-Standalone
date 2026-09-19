using BirdHell.Entities;

namespace BirdHell.Systems;

public sealed class EnemyPopulation
{
    private readonly List<Enemy> _enemies = [];

    public IReadOnlyList<Enemy> Enemies => _enemies;
    public IEnumerable<Enemy> LivingEnemies => _enemies.Where(enemy => enemy.IsAlive);
    public int LivingCount => _enemies.Count(enemy => enemy.IsAlive);
    public bool AllEnemiesDead => _enemies.Count > 0 && LivingCount == 0;

    public void Reset(IEnumerable<Enemy> enemies)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        _enemies.Clear();
        _enemies.AddRange(enemies);
    }

    public IReadOnlyList<ProjectileTarget> CreateProjectileTargets() => LivingEnemies
        .Select(enemy => new ProjectileTarget(enemy, enemy.ColliderCenter, enemy.ColliderRadius))
        .ToArray();
}
