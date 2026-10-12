namespace BirdHell.Presentation;

public enum PlayerVisualState
{
    Idle,
    Run,
    Jump,
    Airborne,
    Roll,
    Crouch,
    Attack,
}

public enum SpriteFlip
{
    None,
    Horizontal,
}

public readonly record struct PlayerVisualSnapshot(bool IsAlive, bool IsGrounded, bool CrouchHeld, float HorizontalSpeed);

/// <summary>Repaired deterministic player visual policy, independent from rendering and gameplay objects.</summary>
public sealed class PlayerAnimationState
{
    private bool _attackPlaying;
    private bool _rollPlaying;
    private readonly AnimationPlayback _locomotionPlayback = new(BirdHellVisuals.PlayerIdle);
    private readonly AnimationPlayback _attackPlayback = new(BirdHellVisuals.PlayerAttack);
    private readonly AnimationPlayback _rollPlayback = new(BirdHellVisuals.PlayerRoll);

    public PlayerAnimationState()
    {
        State = PlayerVisualState.Idle;
        Playback = _locomotionPlayback;
    }

    public PlayerVisualState State { get; private set; }
    public AnimationPlayback Playback { get; private set; }
    public bool ShouldRender { get; private set; } = true;

    public void Update(
        PlayerVisualSnapshot player,
        bool shotStarted,
        bool secondJumpStarted,
        float deltaTime)
    {
        ShouldRender = player.IsAlive;
        if (player.IsGrounded)
        {
            _rollPlaying = false;
            _rollPlayback.Reset();
        }
        if (secondJumpStarted && player.IsAlive && !player.IsGrounded)
        {
            _rollPlaying = true;
            _rollPlayback.Reset();
        }
        if (shotStarted && player.IsAlive)
        {
            _attackPlaying = true;
            _attackPlayback.Reset();
        }

        var desired = SelectState(player);
        SetState(desired);
        if (_attackPlaying)
        {
            _attackPlayback.Advance(deltaTime);
            if (_attackPlayback.IsFinished) _attackPlaying = false;
        }
        if (_rollPlaying)
        {
            _rollPlayback.Advance(deltaTime);
            if (_rollPlayback.IsFinished) _rollPlaying = false;
        }
        if (desired is not PlayerVisualState.Attack and not PlayerVisualState.Roll)
            _locomotionPlayback.Advance(deltaTime);
        if (desired == PlayerVisualState.Jump && _locomotionPlayback.IsFinished)
            SetState(SelectState(player));
        if (desired == PlayerVisualState.Attack && !_attackPlaying ||
            desired == PlayerVisualState.Roll && !_rollPlaying)
            SetState(SelectState(player));
    }

    public void Reset(PlayerVisualSnapshot player)
    {
        _attackPlaying = false;
        _rollPlaying = false;
        State = PlayerVisualState.Idle;
        _attackPlayback.Reset();
        _rollPlayback.Reset();
        _locomotionPlayback.Play(BirdHellVisuals.PlayerIdle, restart: true);
        Playback = _locomotionPlayback;
        Update(player, false, false, 0f);
    }

    public static SpriteFlip FlipForFacing(int facingDirection)
    {
        var desired = facingDirection < 0 ? HorizontalFacing.Left : HorizontalFacing.Right;
        return desired == BirdHellVisuals.PlayerNativeFacing ? SpriteFlip.None : SpriteFlip.Horizontal;
    }

    private PlayerVisualState SelectState(PlayerVisualSnapshot player) => _attackPlaying
        ? PlayerVisualState.Attack
        : _rollPlaying
            ? PlayerVisualState.Roll
            : !player.IsGrounded
                ? _locomotionPlayback.Clip == BirdHellVisuals.PlayerAirborne ||
                  _locomotionPlayback.Clip == BirdHellVisuals.PlayerJump && _locomotionPlayback.IsFinished
                    ? PlayerVisualState.Airborne
                    : PlayerVisualState.Jump
                : player.CrouchHeld
                    ? PlayerVisualState.Crouch
                    : MathF.Abs(player.HorizontalSpeed) > 0.01f
                        ? PlayerVisualState.Run
                        : PlayerVisualState.Idle;

    private void SetState(PlayerVisualState state)
    {
        State = state;
        if (state == PlayerVisualState.Attack)
        {
            Playback = _attackPlayback;
            return;
        }
        if (state == PlayerVisualState.Roll)
        {
            Playback = _rollPlayback;
            return;
        }
        var clip = state switch
        {
            PlayerVisualState.Idle => BirdHellVisuals.PlayerIdle,
            PlayerVisualState.Run => BirdHellVisuals.PlayerRun,
            PlayerVisualState.Jump => BirdHellVisuals.PlayerJump,
            PlayerVisualState.Airborne => BirdHellVisuals.PlayerAirborne,
            PlayerVisualState.Crouch => BirdHellVisuals.PlayerCrouch,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        _locomotionPlayback.Play(clip);
        Playback = _locomotionPlayback;
    }
}

public sealed class WorldAnimationState
{
    public PlayerAnimationState Player { get; } = new();
    public AnimationPlayback Enemy { get; } = new(BirdHellVisuals.Enemy);

    public void AdvanceEnemies(float deltaTime) => Enemy.Advance(deltaTime);

    public void Reset(PlayerVisualSnapshot player)
    {
        Player.Reset(player);
        Enemy.Reset();
    }

    public static int ProjectileFrame(float age) => BirdHellVisuals.Projectile.GetFrameIndex(age);
    public static SpriteFlip EnemyFlip(int facingDirection)
    {
        var desired = facingDirection > 0 ? HorizontalFacing.Right : HorizontalFacing.Left;
        return desired == BirdHellVisuals.EnemyNativeFacing ? SpriteFlip.None : SpriteFlip.Horizontal;
    }
    public static SpriteFlip ProjectileFlip(float directionX) => directionX < 0f ? SpriteFlip.Horizontal : SpriteFlip.None;
}
