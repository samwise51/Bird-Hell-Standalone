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
    private double _enemyVisualTime;

    public PlayerAnimationState Player { get; } = new();
    public double EnemyVisualTime => _enemyVisualTime;

    public void AdvanceEnemies(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        _enemyVisualTime += deltaTime;
    }

    public SpriteFrame EnemyFrame(string runSeed, int enemyId) =>
        BirdHellVisuals.Enemy.Frames[EnemyFlightVisualTiming.FrameIndexAt(runSeed, enemyId, _enemyVisualTime)];

    public void Reset(PlayerVisualSnapshot player)
    {
        Player.Reset(player);
        _enemyVisualTime = 0d;
    }

    public static int ProjectileFrame(float age) => BirdHellVisuals.Projectile.GetFrameIndex(age);
    public static SpriteFlip EnemyFlip(int facingDirection)
    {
        var desired = facingDirection > 0 ? HorizontalFacing.Right : HorizontalFacing.Left;
        return desired == BirdHellVisuals.EnemyNativeFacing ? SpriteFlip.None : SpriteFlip.Horizontal;
    }
    public static SpriteFlip ProjectileFlip(float directionX) => directionX < 0f ? SpriteFlip.Horizontal : SpriteFlip.None;
}

public readonly record struct EnemyFlightVisualParameters(float PhaseOffset, float RateMultiplier);

/// <summary>Pure deterministic visual timing for enemy flight; it never mutates gameplay state.</summary>
public static class EnemyFlightVisualTiming
{
    public const float MinimumRateMultiplier = 0.97f;
    public const float MaximumRateMultiplier = 1.03f;
    private const uint PhaseSalt = 0xA511_E9B3u;
    private const uint RateSalt = 0x63D8_35A7u;
    private const float UnitDenominator = 16_777_216f;

    public static EnemyFlightVisualParameters ForEnemy(string runSeed, int enemyId)
    {
        ArgumentNullException.ThrowIfNull(runSeed);
        if (enemyId < 0) throw new ArgumentOutOfRangeException(nameof(enemyId));
        var phaseUnit = ToUnitInterval(StableMix(runSeed, enemyId, PhaseSalt));
        var rateUnit = ToUnitInterval(StableMix(runSeed, enemyId, RateSalt));
        return new EnemyFlightVisualParameters(
            phaseUnit * BirdHellVisuals.Enemy.Duration,
            MinimumRateMultiplier + rateUnit * (MaximumRateMultiplier - MinimumRateMultiplier));
    }

    public static int FrameIndexAt(string runSeed, int enemyId, double visualTime)
    {
        if (!double.IsFinite(visualTime) || visualTime < 0d)
            throw new ArgumentOutOfRangeException(nameof(visualTime));
        var parameters = ForEnemy(runSeed, enemyId);
        var cycleTime = (visualTime * parameters.RateMultiplier + parameters.PhaseOffset) %
                        BirdHellVisuals.Enemy.Duration;
        return BirdHellVisuals.Enemy.GetFrameIndex((float)cycleTime);
    }

    private static float ToUnitInterval(uint value) => (value & 0x00FF_FFFFu) / UnitDenominator;

    private static uint StableMix(string runSeed, int enemyId, uint salt)
    {
        const uint offset = 2166136261u;
        const uint prime = 16777619u;
        var hash = offset;
        foreach (var character in runSeed)
        {
            hash = (hash ^ (byte)character) * prime;
            hash = (hash ^ (byte)(character >> 8)) * prime;
        }
        hash = (hash ^ 0xFFu) * prime;
        var id = unchecked((uint)enemyId);
        for (var shift = 0; shift < 32; shift += 8) hash = (hash ^ (byte)(id >> shift)) * prime;
        hash ^= salt;

        // Fixed-width avalanche; unlike GetHashCode(), this is stable across processes and runtimes.
        hash ^= hash >> 16;
        hash *= 0x7FEB_352Du;
        hash ^= hash >> 15;
        hash *= 0x846C_A68Bu;
        hash ^= hash >> 16;
        return hash;
    }
}
