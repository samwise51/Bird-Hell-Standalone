using System.Numerics;
using BirdHell.Entities;

namespace BirdHell.Systems;

public readonly record struct PlayerInput(float Horizontal, bool JumpPressed, bool JumpHeld);

public sealed record PlayerMovementSettings
{
    public float MovementSpeed { get; init; } = 8f;
    public float JumpSpeed { get; init; } = 18f;
    public float Gravity { get; init; } = 9.81f;
    public float FallMultiplier { get; init; } = 4.5f;
    public float LowJumpMultiplier { get; init; } = 3f;
    public int MaximumJumps { get; init; } = 2;
}

public sealed class PlayerMovementSystem
{
    private readonly TerrainCollisionSystem _collision;
    private readonly PlayerMovementSettings _settings;

    public PlayerMovementSystem(TerrainCollisionSystem collision, PlayerMovementSettings? settings = null)
    {
        _collision = collision ?? throw new ArgumentNullException(nameof(collision));
        _settings = settings ?? new PlayerMovementSettings();
    }

    public void Step(Player player, PlayerInput input, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f || deltaTime > 0.1f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));

        _collision.RefreshContacts(player);
        if (player.IsGrounded)
        {
            player.JumpsUsed = 0;
            if (player.Velocity.Y <= 0f) player.IsJumping = false;
        }

        var horizontal = Math.Clamp(input.Horizontal, -1f, 1f);
        if (horizontal != 0f) player.FacingDirection = horizontal > 0f ? 1 : -1;
        if (player.IsGrounded && player.GroundNormal.Y < 0.999f)
        {
            var tangent = Vector2.Normalize(new Vector2(player.GroundNormal.Y, -player.GroundNormal.X));
            player.Velocity = tangent * (_settings.MovementSpeed * horizontal);
        }
        else
        {
            player.Velocity = new Vector2(_settings.MovementSpeed * horizontal, player.IsGrounded ? 0f : player.Velocity.Y);
        }

        if (input.JumpPressed && player.JumpsUsed < _settings.MaximumJumps)
        {
            player.Velocity = new Vector2(0f, _settings.JumpSpeed);
            player.JumpsUsed++;
            player.IsGrounded = false;
            player.IsJumping = true;
        }

        if (!player.IsGrounded)
        {
            var multiplier = player.Velocity.Y <= 0f
                ? _settings.FallMultiplier
                : player.Velocity.Y > 0f && !input.JumpHeld ? _settings.LowJumpMultiplier : 1f;
            player.Velocity += new Vector2(0f, -_settings.Gravity * multiplier * deltaTime);
        }

        _collision.MoveAndCollide(player, player.Velocity * deltaTime);
        if (player.IsGrounded && player.Velocity.Y <= 0f)
        {
            player.JumpsUsed = 0;
            player.IsJumping = false;
        }
    }
}
