using System.Numerics;

namespace BirdHell.Entities;

public sealed class Projectile
{
    public Projectile(
        Vector2 position,
        Vector2 direction,
        float speed,
        int damage,
        float colliderRadius,
        float colliderCenterOffset,
        float colliderVerticalOffset,
        float? maximumLifetime = null)
    {
        if (direction.LengthSquared() <= 0f || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y))
            throw new ArgumentException("Direction must be finite and non-zero.", nameof(direction));
        if (!float.IsFinite(speed) || speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed));
        if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        if (!float.IsFinite(colliderRadius) || colliderRadius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(colliderRadius));
        if (!float.IsFinite(colliderCenterOffset)) throw new ArgumentOutOfRangeException(nameof(colliderCenterOffset));
        if (!float.IsFinite(colliderVerticalOffset)) throw new ArgumentOutOfRangeException(nameof(colliderVerticalOffset));
        if (maximumLifetime is <= 0f || maximumLifetime is not null && !float.IsFinite(maximumLifetime.Value))
            throw new ArgumentOutOfRangeException(nameof(maximumLifetime));

        Position = position;
        Direction = Vector2.Normalize(direction);
        Speed = speed;
        Damage = damage;
        ColliderRadius = colliderRadius;
        ColliderCenterOffset = colliderCenterOffset;
        ColliderVerticalOffset = colliderVerticalOffset;
        MaximumLifetime = maximumLifetime;
    }

    public Vector2 Position { get; internal set; }
    public Vector2 Direction { get; }
    public Vector2 Velocity => Direction * Speed;
    public Vector2 ColliderCenter => Position + Direction * ColliderCenterOffset + Vector2.UnitY * ColliderVerticalOffset;
    public float Speed { get; }
    public int Damage { get; }
    public float ColliderRadius { get; }
    public float ColliderCenterOffset { get; }
    public float ColliderVerticalOffset { get; }
    public float? MaximumLifetime { get; }
    public float Age { get; internal set; }
}
