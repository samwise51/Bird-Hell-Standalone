using System.Numerics;
using BirdHell.Entities;
using BirdHell.World.Geometry;

namespace BirdHell.Systems;

public readonly record struct TerrainSegment(Vector2 A, Vector2 B);

public sealed class TerrainCollisionSystem
{
    public const float Skin = 0.0025f;
    public const float GroundProbeDistance = 0.035f;
    public const float MaximumWalkableSlopeDegrees = 45f;
    public static float MinimumGroundNormalY => MathF.Cos(MaximumWalkableSlopeDegrees * MathF.PI / 180f);
    private const int ResolutionIterations = 8;
    private readonly TerrainSegment[] _segments;
    private readonly TerrainOccupancy? _occupancy;

    public TerrainCollisionSystem(CaveGeometry geometry)
        : this(new TerrainOccupancy(geometry))
    {
    }

    public TerrainCollisionSystem(TerrainOccupancy occupancy)
    {
        _occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        _segments = occupancy.BoundarySegments.Where(segment => segment.A != segment.B).ToArray();
    }

    public TerrainCollisionSystem(IEnumerable<TerrainSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        _segments = segments.Where(segment => segment.A != segment.B).ToArray();
    }

    public IReadOnlyList<TerrainSegment> Segments => _segments;

    public void MoveAndCollide(Player player, Vector2 displacement)
    {
        ArgumentNullException.ThrowIfNull(player);
        var maximumStep = MathF.Max(0.025f, player.Collider.Radius * 0.45f);
        var stepCount = Math.Max(1, (int)MathF.Ceiling(displacement.Length() / maximumStep));
        var step = displacement / stepCount;

        for (var substep = 0; substep < stepCount; substep++)
        {
            player.Position += step;
            ResolvePenetrations(player, step);
        }

        RefreshContacts(player);
    }

    public void RefreshContacts(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var grounded = false;
        var bestNormal = Vector2.UnitY;

        foreach (var segment in _segments)
        {
            var contact = GetContact(player.Position, player.Collider, segment, Vector2.Zero);
            if (contact.Distance > player.Collider.Radius + GroundProbeDistance ||
                contact.Normal.Y < MinimumGroundNormalY) continue;
            if (!grounded || contact.Normal.Y > bestNormal.Y)
            {
                grounded = true;
                bestNormal = contact.Normal;
            }
        }

        player.IsGrounded = grounded;
        player.GroundNormal = grounded ? bestNormal : Vector2.UnitY;
    }

    public bool Intersects(Vector2 position, PlayerCollider collider, float tolerance = 0.0001f)
    {
        if (_occupancy is not null && !_occupancy.IsTraversable(position + collider.Offset)) return true;
        foreach (var segment in _segments)
            if (GetContact(position, collider, segment, Vector2.Zero).Distance < collider.Radius - tolerance)
                return true;
        return false;
    }

    public bool TrySweepCircle(Vector2 start, Vector2 displacement, float radius, out float hitFraction)
    {
        if (!float.IsFinite(start.X) || !float.IsFinite(start.Y)) throw new ArgumentOutOfRangeException(nameof(start));
        if (!float.IsFinite(displacement.X) || !float.IsFinite(displacement.Y))
            throw new ArgumentOutOfRangeException(nameof(displacement));
        if (!float.IsFinite(radius) || radius < 0f) throw new ArgumentOutOfRangeException(nameof(radius));

        hitFraction = float.PositiveInfinity;
        foreach (var segment in _segments)
            if (TrySweepPointAgainstSegment(start, displacement, segment, radius, out var candidate) &&
                candidate < hitFraction)
                hitFraction = candidate;
        return float.IsFinite(hitFraction);
    }

    public bool IntersectsCircle(Vector2 center, float radius, float tolerance = 0.0001f)
    {
        if (radius < 0f || !float.IsFinite(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        if (_occupancy is not null) return _occupancy.IntersectsCircle(center, radius, tolerance);
        return _segments.Any(segment => DistanceSquaredToSegment(center, segment) <
                                        (radius - tolerance) * (radius - tolerance));
    }

    public bool MoveCircle(ref Vector2 center, ref Vector2 velocity, Vector2 displacement, float radius)
    {
        if (!TrySweepCircle(center, displacement, radius, out var fraction))
        {
            center += displacement;
            return false;
        }

        var safeFraction = MathF.Max(0f, fraction - Skin / MathF.Max(displacement.Length(), Skin));
        center += displacement * safeFraction;
        var closest = ClosestPoint(center, out var segment);
        var normal = center - closest;
        if (normal.LengthSquared() <= 0.000001f)
        {
            var edge = segment.B - segment.A;
            normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
            if (Vector2.Dot(normal, displacement) > 0f) normal = -normal;
        }
        else normal = Vector2.Normalize(normal);
        var inward = Vector2.Dot(velocity, normal);
        if (inward < 0f) velocity -= normal * inward;
        return true;
    }

    public static float DistanceSquaredToSegment(Vector2 point, TerrainSegment segment)
    {
        var edge = segment.B - segment.A;
        var lengthSquared = edge.LengthSquared();
        if (lengthSquared <= 0.000001f) return Vector2.DistanceSquared(point, segment.A);
        var t = Math.Clamp(Vector2.Dot(point - segment.A, edge) / lengthSquared, 0f, 1f);
        return Vector2.DistanceSquared(point, segment.A + edge * t);
    }

    private Vector2 ClosestPoint(Vector2 point, out TerrainSegment closestSegment)
    {
        var best = float.PositiveInfinity;
        var result = point;
        closestSegment = default;
        foreach (var segment in _segments)
        {
            var edge = segment.B - segment.A;
            var t = edge.LengthSquared() <= 0.000001f
                ? 0f
                : Math.Clamp(Vector2.Dot(point - segment.A, edge) / edge.LengthSquared(), 0f, 1f);
            var candidate = segment.A + edge * t;
            var distance = Vector2.DistanceSquared(point, candidate);
            if (distance >= best) continue;
            best = distance;
            result = candidate;
            closestSegment = segment;
        }
        return result;
    }

    internal static bool TrySweepPointAgainstSegment(
        Vector2 start,
        Vector2 displacement,
        TerrainSegment segment,
        float radius,
        out float hitFraction)
    {
        const float epsilon = 0.000001f;
        var earliestHit = float.PositiveInfinity;
        hitFraction = earliestHit;
        var edge = segment.B - segment.A;
        var edgeLength = edge.Length();
        if (edgeLength <= epsilon) return TrySweepPointAgainstCircle(start, displacement, segment.A, radius, out hitFraction);

        var tangent = edge / edgeLength;
        var normal = new Vector2(-tangent.Y, tangent.X);
        var relative = start - segment.A;
        var along = Vector2.Dot(relative, tangent);
        var away = Vector2.Dot(relative, normal);
        var closestAlong = Math.Clamp(along, 0f, edgeLength);
        var closest = segment.A + tangent * closestAlong;
        if (Vector2.DistanceSquared(start, closest) <= radius * radius)
        {
            hitFraction = 0f;
            return true;
        }

        var alongVelocity = Vector2.Dot(displacement, tangent);
        var normalVelocity = Vector2.Dot(displacement, normal);
        if (MathF.Abs(normalVelocity) > epsilon)
        {
            TestStripBoundary(radius);
            TestStripBoundary(-radius);
        }

        if (TrySweepPointAgainstCircle(start, displacement, segment.A, radius, out var firstEnd))
            earliestHit = MathF.Min(earliestHit, firstEnd);
        if (TrySweepPointAgainstCircle(start, displacement, segment.B, radius, out var secondEnd))
            earliestHit = MathF.Min(earliestHit, secondEnd);
        hitFraction = earliestHit;
        return float.IsFinite(earliestHit);

        void TestStripBoundary(float boundary)
        {
            var time = (boundary - away) / normalVelocity;
            if (time < 0f || time > 1f) return;
            var positionAlongEdge = along + alongVelocity * time;
            if (positionAlongEdge >= 0f && positionAlongEdge <= edgeLength)
                earliestHit = MathF.Min(earliestHit, time);
        }
    }

    internal static bool TrySweepPointAgainstCircle(
        Vector2 start,
        Vector2 displacement,
        Vector2 center,
        float radius,
        out float hitFraction)
    {
        const float epsilon = 0.000001f;
        hitFraction = float.PositiveInfinity;
        var relative = start - center;
        var radiusSquared = radius * radius;
        if (relative.LengthSquared() <= radiusSquared)
        {
            hitFraction = 0f;
            return true;
        }

        var a = displacement.LengthSquared();
        if (a <= epsilon) return false;
        var b = 2f * Vector2.Dot(relative, displacement);
        var c = relative.LengthSquared() - radiusSquared;
        var discriminant = b * b - 4f * a * c;
        if (discriminant < 0f) return false;
        var time = (-b - MathF.Sqrt(discriminant)) / (2f * a);
        if (time < 0f || time > 1f) return false;
        hitFraction = time;
        return true;
    }

    private void ResolvePenetrations(Player player, Vector2 movement)
    {
        for (var iteration = 0; iteration < ResolutionIterations; iteration++)
        {
            var deepest = 0f;
            var correctionNormal = Vector2.Zero;

            foreach (var segment in _segments)
            {
                var contact = GetContact(player.Position, player.Collider, segment, movement);
                var penetration = player.Collider.Radius - contact.Distance;
                if (penetration <= deepest) continue;
                deepest = penetration;
                correctionNormal = contact.Normal;
            }

            if (deepest <= 0f) break;
            player.Position += correctionNormal * (deepest + Skin);
            var inwardSpeed = Vector2.Dot(player.Velocity, correctionNormal);
            if (inwardSpeed < 0f) player.Velocity -= correctionNormal * inwardSpeed;
        }
    }

    private static Contact GetContact(
        Vector2 position,
        PlayerCollider collider,
        TerrainSegment terrain,
        Vector2 movement)
    {
        var center = position + collider.Offset;
        var coreOffset = new Vector2(0f, collider.CoreHalfHeight);
        ClosestPointsOnSegments(center - coreOffset, center + coreOffset, terrain.A, terrain.B, out var capsulePoint, out var terrainPoint);
        var separation = capsulePoint - terrainPoint;
        var distance = separation.Length();
        if (distance > 0.000001f) return new Contact(distance, separation / distance);

        var direction = terrain.B - terrain.A;
        var normal = Vector2.Normalize(new Vector2(-direction.Y, direction.X));
        if (Vector2.Dot(normal, center - (terrain.A + terrain.B) / 2f) < 0f) normal = -normal;
        if (movement != Vector2.Zero && Vector2.Dot(normal, movement) > 0f) normal = -normal;
        return new Contact(0f, normal);
    }

    private static void ClosestPointsOnSegments(
        Vector2 p1,
        Vector2 q1,
        Vector2 p2,
        Vector2 q2,
        out Vector2 point1,
        out Vector2 point2)
    {
        const float epsilon = 0.000001f;
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector2.Dot(d1, d1);
        var e = Vector2.Dot(d2, d2);
        var f = Vector2.Dot(d2, r);
        float s;
        float t;

        if (a <= epsilon)
        {
            s = 0f;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            var c = Vector2.Dot(d1, r);
            if (e <= epsilon)
            {
                t = 0f;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                var b = Vector2.Dot(d1, d2);
                var denominator = a * e - b * b;
                s = denominator == 0f ? 0f : Math.Clamp((b * f - c * e) / denominator, 0f, 1f);
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }

        point1 = p1 + d1 * s;
        point2 = p2 + d2 * t;
    }

    private readonly record struct Contact(float Distance, Vector2 Normal);
}
