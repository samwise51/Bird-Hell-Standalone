using System.Numerics;
using BirdHell.Entities;
using BirdHell.World.Geometry;

namespace BirdHell.Systems;

public static class PlayerSpawnSystem
{
    private const float SpawnClearance = 0.01f;

    public static Vector2 FindSafeSpawn(CaveGeometry geometry, PlayerCollider collider)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var collision = new TerrainCollisionSystem(geometry);
        var primaryOutline = geometry.Outlines
            .OrderByDescending(outline => BoundsArea(outline.Points))
            .FirstOrDefault() ?? throw new InvalidOperationException("Cave geometry has no terrain boundary outlines.");
        var minX = primaryOutline.Points.Min(point => point.X);
        var maxX = primaryOutline.Points.Max(point => point.X);
        var minY = primaryOutline.Points.Min(point => point.Y);
        var maxY = primaryOutline.Points.Max(point => point.Y);
        var desired = new Vector2((minX + maxX) / 2f - 5f, (minY + maxY) / 2f - 5f);

        var candidates = new List<Vector2>();
        for (var x = minX + collider.Width; x <= maxX - collider.Width; x += 0.25f)
        {
            foreach (var segment in collision.Segments)
            {
                var segmentMinX = MathF.Min(segment.A.X, segment.B.X);
                var segmentMaxX = MathF.Max(segment.A.X, segment.B.X);
                if (segmentMaxX - segmentMinX < 0.0001f || x < segmentMinX || x > segmentMaxX) continue;
                var t = (x - segment.A.X) / (segment.B.X - segment.A.X);
                var floorY = segment.A.Y + (segment.B.Y - segment.A.Y) * t;
                var position = new Vector2(x - collider.Offset.X, floorY - collider.BottomOffset + SpawnClearance);
                if (collision.Intersects(position, collider)) continue;
                if (!CapsuleSamplesAreOpen(position, collider, geometry)) continue;
                candidates.Add(position);
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("No safe player spawn could be derived from cave boundary geometry.");

        return candidates
            .OrderBy(position => MathF.Abs(position.X - desired.X))
            .ThenBy(position => MathF.Abs(position.Y - desired.Y))
            .ThenBy(position => position.X)
            .ThenBy(position => position.Y)
            .First();
    }

    private static bool CapsuleSamplesAreOpen(Vector2 position, PlayerCollider collider, CaveGeometry geometry)
    {
        var center = position + collider.Offset;
        var core = collider.CoreHalfHeight;
        var radius = collider.Radius * 0.85f;
        var samples = new[]
        {
            center,
            center + new Vector2(0f, core),
            center - new Vector2(0f, core),
            center + new Vector2(radius, core),
            center + new Vector2(-radius, core),
            center + new Vector2(radius, -core),
            center + new Vector2(-radius, -core),
        };
        return samples.All(sample => !IsPointInSolid(sample, geometry));
    }

    private static bool IsPointInSolid(Vector2 point, CaveGeometry geometry)
    {
        foreach (var triangle in geometry.Triangles)
        {
            var a = geometry.Vertices[triangle.A];
            var b = geometry.Vertices[triangle.B];
            var c = geometry.Vertices[triangle.C];
            if (PointInTriangle(point, new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), new Vector2(c.X, c.Y))) return true;
        }
        return false;
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(point - b, a - b);
        var d2 = Cross(point - c, b - c);
        var d3 = Cross(point - a, c - a);
        var hasNegative = d1 < -0.0001f || d2 < -0.0001f || d3 < -0.0001f;
        var hasPositive = d1 > 0.0001f || d2 > 0.0001f || d3 > 0.0001f;
        return !(hasNegative && hasPositive);
    }

    private static float Cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

    private static float BoundsArea(IReadOnlyCollection<CaveVertex> points) =>
        (points.Max(point => point.X) - points.Min(point => point.X)) *
        (points.Max(point => point.Y) - points.Min(point => point.Y));
}
