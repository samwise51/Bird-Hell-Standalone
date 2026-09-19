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

public sealed class Player
{
    public Player(Vector2 position, PlayerCollider? collider = null)
    {
        Position = position;
        Collider = collider ?? PlayerCollider.Original;
    }

    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public PlayerCollider Collider { get; }
    public bool IsGrounded { get; internal set; }
    public bool IsJumping { get; internal set; }
    public Vector2 GroundNormal { get; internal set; } = Vector2.UnitY;
    public int JumpsUsed { get; internal set; }

    public Vector2 ColliderCenter => Position + Collider.Offset;

    public void Reset(Vector2 position)
    {
        Position = position;
        Velocity = Vector2.Zero;
        IsGrounded = false;
        IsJumping = false;
        GroundNormal = Vector2.UnitY;
        JumpsUsed = 0;
    }
}
