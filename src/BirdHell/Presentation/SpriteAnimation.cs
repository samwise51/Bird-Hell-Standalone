using System.Numerics;

namespace BirdHell.Presentation;

public readonly record struct PixelRectangle(int X, int Y, int Width, int Height);

public enum PixelCoordinateOrigin
{
    TopLeft,
    BottomLeft,
}

public enum SpriteTextureFilter
{
    Point,
    Linear,
}

public enum HorizontalFacing
{
    Left = -1,
    Right = 1,
}

public readonly record struct SpriteRegion(
    string TextureId,
    PixelRectangle Source,
    Vector2 Pivot,
    float PixelsPerUnit,
    float WorldScale,
    SpriteTextureFilter Filter,
    PixelCoordinateOrigin CoordinateOrigin = PixelCoordinateOrigin.TopLeft)
{
    public Vector2 WorldSize => new(Source.Width * WorldScale / PixelsPerUnit, Source.Height * WorldScale / PixelsPerUnit);
}

public readonly record struct SpriteFrame(SpriteRegion Region, float Duration);

/// <summary>Immutable sprite animation metadata with no graphics-device dependency.</summary>
public sealed class SpriteAnimationClip
{
    public SpriteAnimationClip(string name, IReadOnlyList<SpriteFrame> frames, bool looping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("An animation requires at least one frame.", nameof(frames));
        if (frames.Any(frame => !float.IsFinite(frame.Duration) || frame.Duration <= 0f))
            throw new ArgumentException("Every animation frame requires a finite positive duration.", nameof(frames));
        Name = name;
        Frames = frames;
        Looping = looping;
        Duration = frames.Sum(frame => frame.Duration);
    }

    public string Name { get; }
    public IReadOnlyList<SpriteFrame> Frames { get; }
    public bool Looping { get; }
    public float Duration { get; }

    public int GetFrameIndex(float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0f) throw new ArgumentOutOfRangeException(nameof(elapsed));
        var time = Looping ? elapsed % Duration : MathF.Min(elapsed, MathF.BitDecrement(Duration));
        var accumulated = 0f;
        for (var index = 0; index < Frames.Count; index++)
        {
            accumulated += Frames[index].Duration;
            if (time < accumulated) return index;
        }
        return Frames.Count - 1;
    }
}

/// <summary>Small value-only playback clock. It never retains gameplay entities.</summary>
public sealed class AnimationPlayback
{
    public AnimationPlayback(SpriteAnimationClip clip) => Clip = clip ?? throw new ArgumentNullException(nameof(clip));

    public SpriteAnimationClip Clip { get; private set; }
    public float Elapsed { get; private set; }
    public bool IsFinished => !Clip.Looping && Elapsed >= Clip.Duration;
    public int CurrentFrameIndex => Clip.GetFrameIndex(Elapsed);
    public SpriteFrame CurrentFrame => Clip.Frames[CurrentFrameIndex];

    public void Play(SpriteAnimationClip clip, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!ReferenceEquals(Clip, clip) || restart)
        {
            Clip = clip;
            Elapsed = 0f;
        }
    }

    public void Advance(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        Elapsed = Clip.Looping ? (Elapsed + deltaTime) % Clip.Duration : MathF.Min(Elapsed + deltaTime, Clip.Duration);
    }

    public void Reset() => Elapsed = 0f;
}
