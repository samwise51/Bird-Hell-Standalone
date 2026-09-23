using System.Numerics;
using BirdHell.World.Geometry;

namespace BirdHell.Systems;

public enum TerrainPointClassification
{
    Exterior,
    Solid,
    Traversable,
}

/// <summary>
/// Renderer-independent occupancy queries over the authoritative marching-squares geometry.
/// Filled triangles are solid, points inside the geometry bounds but outside those triangles are
/// traversable cave space, and points beyond the geometry bounds are exterior.
/// </summary>
public sealed class TerrainOccupancy
{
    private const float PointTolerance = 0.0001f;
    private readonly TriangleBounds[] _triangles;
    private readonly TerrainSegment[] _boundarySegments;

    public TerrainOccupancy(CaveGeometry geometry)
    {
        Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _triangles = geometry.Triangles.Select(triangle =>
        {
            var a = ToVector(geometry.Vertices[triangle.A]);
            var b = ToVector(geometry.Vertices[triangle.B]);
            var c = ToVector(geometry.Vertices[triangle.C]);
            return new TriangleBounds(a, b, c);
        }).ToArray();
        _boundarySegments = geometry.Outlines.SelectMany(ToSegments)
            .Where(segment => segment.A != segment.B)
            .ToArray();
    }

    public CaveGeometry Geometry { get; }
    public IReadOnlyList<TerrainSegment> BoundarySegments => _boundarySegments;

    public TerrainPointClassification Classify(Vector2 point)
    {
        ValidatePoint(point);
        if (point.X < Geometry.MinX || point.X > Geometry.MaxX ||
            point.Y < Geometry.MinY || point.Y > Geometry.MaxY)
            return TerrainPointClassification.Exterior;
        return IsSolid(point) ? TerrainPointClassification.Solid : TerrainPointClassification.Traversable;
    }

    public bool IsExterior(Vector2 point)
    {
        ValidatePoint(point);
        return point.X < Geometry.MinX || point.X > Geometry.MaxX ||
               point.Y < Geometry.MinY || point.Y > Geometry.MaxY;
    }

    public bool IsSolid(Vector2 point)
    {
        ValidatePoint(point);
        foreach (var triangle in _triangles)
        {
            if (!triangle.BoundsContain(point)) continue;
            if (PointInTriangle(point, triangle.A, triangle.B, triangle.C)) return true;
        }
        return false;
    }

    public bool IsTraversable(Vector2 point) => Classify(point) == TerrainPointClassification.Traversable;

    public bool HasClearance(Vector2 point, float clearance)
    {
        if (!float.IsFinite(clearance) || clearance < 0f)
            throw new ArgumentOutOfRangeException(nameof(clearance));
        if (!IsTraversable(point)) return false;
        var clearanceSquared = clearance * clearance;
        return _boundarySegments.All(segment =>
            TerrainCollisionSystem.DistanceSquaredToSegment(point, segment) > clearanceSquared);
    }

    public float GetBoundaryClearance(Vector2 point)
    {
        ValidatePoint(point);
        if (_boundarySegments.Length == 0) return float.PositiveInfinity;
        var minimumSquared = float.PositiveInfinity;
        foreach (var segment in _boundarySegments)
            minimumSquared = MathF.Min(minimumSquared,
                TerrainCollisionSystem.DistanceSquaredToSegment(point, segment));
        return MathF.Sqrt(minimumSquared);
    }

    public bool IntersectsCircle(Vector2 center, float radius, float tolerance = 0.0001f)
    {
        if (!float.IsFinite(radius) || radius < 0f) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!float.IsFinite(tolerance) || tolerance < 0f) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (!IsTraversable(center)) return true;
        var effectiveRadius = MathF.Max(0f, radius - tolerance);
        var radiusSquared = effectiveRadius * effectiveRadius;
        return _boundarySegments.Any(segment =>
            TerrainCollisionSystem.DistanceSquaredToSegment(center, segment) < radiusSquared);
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        var d1 = Cross(point - b, a - b);
        var d2 = Cross(point - c, b - c);
        var d3 = Cross(point - a, c - a);
        var hasNegative = d1 < -PointTolerance || d2 < -PointTolerance || d3 < -PointTolerance;
        var hasPositive = d1 > PointTolerance || d2 > PointTolerance || d3 > PointTolerance;
        return !(hasNegative && hasPositive);
    }

    private static float Cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

    private static IEnumerable<TerrainSegment> ToSegments(CaveOutline outline)
    {
        for (var i = 0; i < outline.Points.Count - 1; i++)
            yield return new TerrainSegment(ToVector(outline.Points[i]), ToVector(outline.Points[i + 1]));
    }

    private static Vector2 ToVector(CaveVertex point) => new(point.X, point.Y);

    private static void ValidatePoint(Vector2 point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
    }

    private readonly record struct TriangleBounds(Vector2 A, Vector2 B, Vector2 C)
    {
        private float MinX { get; } = MathF.Min(A.X, MathF.Min(B.X, C.X)) - PointTolerance;
        private float MaxX { get; } = MathF.Max(A.X, MathF.Max(B.X, C.X)) + PointTolerance;
        private float MinY { get; } = MathF.Min(A.Y, MathF.Min(B.Y, C.Y)) - PointTolerance;
        private float MaxY { get; } = MathF.Max(A.Y, MathF.Max(B.Y, C.Y)) + PointTolerance;

        public bool BoundsContain(Vector2 point) =>
            point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
    }
}
