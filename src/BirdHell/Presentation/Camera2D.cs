using System.Numerics;

namespace BirdHell.Presentation;

/// <summary>Renderer-independent logical camera for the active MainScreen.</summary>
public sealed class Camera2D
{
    public const float FixedStepInterpolation = 0.15f;
    public const float VerticalViewSpan = 10f;
    public const float ImpactShakeDuration = 0.10f;
    public const float ImpactShakeAmplitude = 0.075f;
    public static readonly Vector2 TargetOffset = new(0f, 1f);

    public Vector2 Position { get; private set; } = Vector2.Zero;
    public Vector2 ShakeOffset { get; private set; }
    public float ShakeTimeRemaining { get; private set; }
    public bool IsShaking => ShakeTimeRemaining > 0f;

    private Vector2 _shakeDirection = Vector2.UnitX;

    public void Step(Vector2 playerPosition)
    {
        if (!float.IsFinite(playerPosition.X) || !float.IsFinite(playerPosition.Y))
            throw new ArgumentOutOfRangeException(nameof(playerPosition));

        Position = Vector2.Lerp(Position, playerPosition + TargetOffset, FixedStepInterpolation);
    }

    public void TriggerImpact(Vector2 forceDirection)
    {
        var direction = forceDirection.LengthSquared() > 0.000001f &&
                        float.IsFinite(forceDirection.X) && float.IsFinite(forceDirection.Y)
            ? Vector2.Normalize(forceDirection)
            : Vector2.UnitX;
        _shakeDirection = Vector2.Normalize(new Vector2(-direction.Y + direction.X * 0.35f,
                                                        direction.X + direction.Y * 0.35f));
        ShakeTimeRemaining = ImpactShakeDuration;
        UpdateShakeOffset();
    }

    public void AdvanceImpact(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        ShakeTimeRemaining = deltaTime >= ShakeTimeRemaining ? 0f : ShakeTimeRemaining - deltaTime;
        UpdateShakeOffset();
    }

    public void ResetImpact()
    {
        ShakeTimeRemaining = 0f;
        ShakeOffset = Vector2.Zero;
    }

    public CameraView GetView(int viewportWidth, int viewportHeight) =>
        CameraView.Create(Position + ShakeOffset, viewportWidth, viewportHeight);

    private void UpdateShakeOffset()
    {
        if (!IsShaking)
        {
            ShakeOffset = Vector2.Zero;
            return;
        }
        var progress = 1f - ShakeTimeRemaining / ImpactShakeDuration;
        var envelope = ShakeTimeRemaining / ImpactShakeDuration;
        var sign = MathF.Sin(progress * MathF.PI * 8f) >= 0f ? 1f : -1f;
        ShakeOffset = _shakeDirection * (ImpactShakeAmplitude * envelope * sign);
    }
}

public readonly record struct CameraView(
    Vector2 Center,
    float HorizontalSpan,
    float VerticalSpan,
    float Left,
    float Right,
    float Bottom,
    float Top)
{
    public static CameraView Create(Vector2 center, int viewportWidth, int viewportHeight)
    {
        if (viewportWidth <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight <= 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));

        var horizontalSpan = Camera2D.VerticalViewSpan * viewportWidth / viewportHeight;
        var halfWidth = horizontalSpan / 2f;
        var halfHeight = Camera2D.VerticalViewSpan / 2f;
        return new CameraView(
            center,
            horizontalSpan,
            Camera2D.VerticalViewSpan,
            center.X - halfWidth,
            center.X + halfWidth,
            center.Y - halfHeight,
            center.Y + halfHeight);
    }
}
