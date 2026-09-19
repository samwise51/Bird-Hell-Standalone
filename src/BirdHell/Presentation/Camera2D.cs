using System.Numerics;

namespace BirdHell.Presentation;

/// <summary>Renderer-independent logical camera for the active MainScreen.</summary>
public sealed class Camera2D
{
    public const float FixedStepInterpolation = 0.15f;
    public const float VerticalViewSpan = 10f;
    public static readonly Vector2 TargetOffset = new(0f, 1f);

    public Vector2 Position { get; private set; } = Vector2.Zero;

    public void Step(Vector2 playerPosition)
    {
        if (!float.IsFinite(playerPosition.X) || !float.IsFinite(playerPosition.Y))
            throw new ArgumentOutOfRangeException(nameof(playerPosition));

        Position = Vector2.Lerp(Position, playerPosition + TargetOffset, FixedStepInterpolation);
    }

    public CameraView GetView(int viewportWidth, int viewportHeight) =>
        CameraView.Create(Position, viewportWidth, viewportHeight);
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
