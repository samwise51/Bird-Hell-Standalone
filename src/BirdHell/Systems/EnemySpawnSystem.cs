using System.Numerics;
using BirdHell.Entities;
using BirdHell.World;
using BirdHell.World.Geometry;

namespace BirdHell.Systems;

public static class EnemySpawnSystem
{
    public const int OriginalEnemyCount = 15;

    public static IReadOnlyList<Enemy> Spawn(
        CaveGeometry geometry,
        NavigationGrid navigationGrid,
        TerrainCollisionSystem terrain,
        string seed,
        int count = OriginalEnemyCount)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(navigationGrid);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(seed);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (geometry.Outlines.Count == 0) throw new InvalidOperationException("Cave geometry has no outline for enemy spawning.");

        var firstOutline = geometry.Outlines[0];
        var reference = new Vector2(
            (firstOutline.Points.Min(point => point.X) + firstOutline.Points.Max(point => point.X)) / 2f,
            (firstOutline.Points.Min(point => point.Y) + firstOutline.Points.Max(point => point.Y)) / 2f);
        if (!navigationGrid.TryFindNearestWalkable(reference, AStarPathfinder.MaximumNearestNodeDistance, out var spawnCell))
            throw new InvalidOperationException("No walkable navigation node exists near the original enemy spawn reference.");

        var stableRandom = new Random(StableHash(seed));
        var center = navigationGrid.CellToWorld(spawnCell);
        var enemies = new List<Enemy>(count);
        const float goldenAngle = 2.39996323f;
        for (var index = 0; index < count; index++)
        {
            var angle = index * goldenAngle;
            var offset = index == 0 ? Vector2.Zero : new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (0.0125f * MathF.Sqrt(index));
            var candidateCenter = center + offset;
            if (terrain.IntersectsCircle(candidateCenter, Enemy.OriginalColliderRadius)) candidateCenter = center;
            enemies.Add(new Enemy(index, candidateCenter - Enemy.OriginalColliderOffset, stableRandom.Next(2, 10)));
        }
        return enemies;
    }

    private static int StableHash(string value)
    {
        const uint prime = 16777619;
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return unchecked((int)hash);
    }
}
