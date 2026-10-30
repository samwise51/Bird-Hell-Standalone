using System.Numerics;

namespace BirdHell.Systems;

public enum HitSourceCategory
{
    Unspecified,
    Projectile,
    EnemyContact,
    Melee,
    DiveBomb,
}

public enum HitVictimCategory
{
    Player,
    Enemy,
}

/// <summary>Everything an actor needs to accept damage and begin a reusable hit reaction.</summary>
public readonly record struct HitContext(
    int Damage,
    Vector2 SourcePosition,
    Vector2 ImpactPosition,
    Vector2 ForceDirection,
    float KnockbackSpeed,
    HitSourceCategory SourceCategory)
{
    public Vector2 NormalizedForceDirection => NormalizeOrZero(ForceDirection);

    public static Vector2 DirectionAwayFrom(Vector2 sourcePosition, Vector2 victimPosition, Vector2 fallback)
    {
        var away = victimPosition - sourcePosition;
        return NormalizeOrZero(away.LengthSquared() > 0.000001f ? away : fallback);
    }

    private static Vector2 NormalizeOrZero(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && value.LengthSquared() > 0.000001f
            ? Vector2.Normalize(value)
            : Vector2.Zero;
}

/// <summary>Presentation-safe description emitted only after damage was accepted.</summary>
public readonly record struct AcceptedHit(
    HitVictimCategory VictimCategory,
    Vector2 SourcePosition,
    Vector2 ImpactPosition,
    Vector2 ForceDirection,
    HitSourceCategory SourceCategory);

public static class HitReactionSettings
{
    public const float ProjectileEnemyKnockbackSpeed = 5.5f;
    public const float EnemyContactPlayerKnockbackSpeed = 6f;
    public const float PlayerVelocityRetentionPerSecond = 0.08f;
    public const float EnemyVelocityRetentionPerSecond = 0.06f;
}
