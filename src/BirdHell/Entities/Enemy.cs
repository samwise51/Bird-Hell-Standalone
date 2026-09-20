using System.Numerics;
using BirdHell.Systems;

namespace BirdHell.Entities;

public sealed class Enemy : IDamageable
{
    public const int DefaultMaximumHealth = 100;
    public const float OriginalColliderRadius = 0.45807048f;
    public static readonly Vector2 OriginalColliderOffset = new(-0.15384216f, -0.07692099f);

    public Enemy(int id, Vector2 position, int maximumSpeed)
    {
        if (maximumSpeed is < 2 or > 9) throw new ArgumentOutOfRangeException(nameof(maximumSpeed));
        Id = id;
        Position = position;
        MaximumSpeed = maximumSpeed;
    }

    public int Id { get; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public float ColliderRadius { get; } = OriginalColliderRadius;
    public Vector2 ColliderOffset { get; } = OriginalColliderOffset;
    public Vector2 ColliderCenter => Position + ColliderOffset;
    public int MaximumHealth { get; } = DefaultMaximumHealth;
    public int Health { get; private set; } = DefaultMaximumHealth;
    public int MaximumSpeed { get; }
    public float ContactTimer { get; set; }
    public bool IsAlive { get; private set; } = true;
    public bool CanTakeDamage => IsAlive;
    public int FacingDirection { get; set; } = 1;
    public float DamageFlashTime { get; private set; }
    public float RepathTimer { get; set; } = EnemyNavigationSystem.RepathInterval;
    public IReadOnlyList<Vector2> Path { get; set; } = Array.Empty<Vector2>();
    public int PathIndex { get; set; }
    public int SuccessfulRepathCount { get; set; }
    public Vector2 SteeringTarget { get; set; }

    public void UpdateFacingFromHorizontalMovement(float horizontalVelocity)
    {
        if (!float.IsFinite(horizontalVelocity)) throw new ArgumentOutOfRangeException(nameof(horizontalVelocity));
        if (horizontalVelocity >= 0.01f) FacingDirection = 1;
        else if (horizontalVelocity <= -0.01f) FacingDirection = -1;
    }

    public void TakeDamage(int damage)
    {
        if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        if (!IsAlive || damage == 0) return;
        Health = Math.Max(0, Health - damage);
        DamageFlashTime = 0.15f;
        if (Health == 0)
        {
            IsAlive = false;
            Velocity = Vector2.Zero;
            Path = Array.Empty<Vector2>();
            PathIndex = 0;
        }
    }

    public void AdvanceFeedback(float deltaTime) => DamageFlashTime = MathF.Max(0f, DamageFlashTime - deltaTime);
}
