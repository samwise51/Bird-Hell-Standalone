using System.Numerics;

namespace BirdHell.Entities;

public readonly record struct PlayerCollider(float Width, float Height, Vector2 Offset)
{
    public static PlayerCollider Original { get; } = new(
        0.073097564f * 4f,
        0.29390925f * 4f,
        new Vector2(0.0028309114f * 4f, -0.032838672f * 4f));

    public float Radius => Width / 2f;
    public float CoreHalfHeight => MathF.Max(0f, (Height - Width) / 2f);
    public float BottomOffset => Offset.Y - Height / 2f;
}

public sealed class Player : Systems.IDamageable
{
    public const int DefaultMaximumHealth = 100;
    public const float DamageFlashDuration = 0.5f;

    public Player(Vector2 position, PlayerCollider? collider = null, bool isInvincible = false)
    {
        Position = position;
        Collider = collider ?? PlayerCollider.Original;
        IsInvincible = isInvincible;
    }

    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public PlayerCollider Collider { get; }
    public bool IsGrounded { get; internal set; }
    public bool IsJumping { get; internal set; }
    public Vector2 GroundNormal { get; internal set; } = Vector2.UnitY;
    public int JumpsUsed { get; internal set; }
    public bool SecondJumpActive { get; internal set; }
    public float SecondJumpElapsed { get; internal set; }
    public int FacingDirection { get; internal set; } = 1;
    public int MaximumHealth { get; } = DefaultMaximumHealth;
    public int Health { get; private set; } = DefaultMaximumHealth;
    public bool IsAlive { get; private set; } = true;
    public bool IsInvincible { get; }
    public bool CanTakeDamage => IsAlive && !IsInvincible;
    public float DamageFlashTime { get; private set; }

    public Vector2 ColliderCenter => Position + Collider.Offset;

    public Systems.DamageResult TakeDamage(int damage)
    {
        if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
        if (!CanTakeDamage || damage == 0) return default;
        Health = Math.Max(0, Health - damage);
        DamageFlashTime = DamageFlashDuration;
        var killed = Health == 0;
        if (killed)
        {
            IsAlive = false;
            Velocity = Vector2.Zero;
            SecondJumpActive = false;
            SecondJumpElapsed = 0f;
        }
        return new Systems.DamageResult(true, killed);
    }

    public void AdvanceFeedback(float deltaTime) =>
        DamageFlashTime = deltaTime >= DamageFlashTime ? 0f : DamageFlashTime - deltaTime;

    public void Reset(Vector2 position)
    {
        Position = position;
        Velocity = Vector2.Zero;
        IsGrounded = false;
        IsJumping = false;
        GroundNormal = Vector2.UnitY;
        JumpsUsed = 0;
        SecondJumpActive = false;
        SecondJumpElapsed = 0f;
        FacingDirection = 1;
        Health = MaximumHealth;
        IsAlive = true;
        DamageFlashTime = 0f;
    }
}
