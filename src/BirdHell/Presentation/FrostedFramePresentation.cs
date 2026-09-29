using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BirdHell.Presentation;

public readonly record struct FrostedFrameDimensions(
    int Width,
    int Height,
    int FirstPassWidth,
    int FirstPassHeight,
    int SecondPassWidth,
    int SecondPassHeight)
{
    public static FrostedFrameDimensions Calculate(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return new FrostedFrameDimensions(
            width,
            height,
            Math.Max(1, width / 4),
            Math.Max(1, height / 4),
            Math.Max(1, width / 8),
            Math.Max(1, height / 8));
    }
}

/// <summary>Captures gameplay and presents a stable two-stage downsampled pause background.</summary>
public sealed class FrostedFramePresentation : IDisposable
{
    private readonly GraphicsDevice _graphics;
    private readonly SpriteBatch _batch;
    private RenderTarget2D? _scene;
    private RenderTarget2D? _firstPass;
    private RenderTarget2D? _secondPass;
    private FrostedFrameDimensions _dimensions;
    private bool _disposed;

    public FrostedFramePresentation(GraphicsDevice graphics)
    {
        _graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
        _batch = new SpriteBatch(graphics);
    }

    public bool HasCapturedFrame { get; private set; }

    public bool HasCompatibleFrame(int width, int height) =>
        HasCapturedFrame && _scene is not null && !_scene.IsDisposed &&
        _dimensions.Width == width && _dimensions.Height == height;

    public void BeginCapture(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureTargets(width, height);
        HasCapturedFrame = false;
        _graphics.SetRenderTarget(_scene);
    }

    public void EndCapture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _graphics.SetRenderTarget(null);
        HasCapturedFrame = _scene is not null;
    }

    public void PresentSharp(Rectangle destination)
    {
        if (!HasCapturedFrame || _scene is null) return;
        _graphics.SetRenderTarget(null);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
        _batch.Draw(_scene, destination, Color.White);
        _batch.End();
    }

    public void PresentFrozen(Rectangle destination)
    {
        if (!HasCapturedFrame || _scene is null || _firstPass is null || _secondPass is null) return;

        _graphics.SetRenderTarget(_firstPass);
        _graphics.Clear(Color.Black);
        DrawTexture(_scene, _firstPass.Bounds);

        _graphics.SetRenderTarget(_secondPass);
        _graphics.Clear(Color.Black);
        DrawTexture(_firstPass, _secondPass.Bounds);

        _graphics.SetRenderTarget(null);
        _graphics.Clear(Color.Black);
        _batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp);
        _batch.Draw(_secondPass, destination, new Color(205, 210, 222));
        _batch.End();
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        _batch.Draw(_secondPass, destination, new Color(17, 10, 34, 92));
        _batch.End();
    }

    public void Release()
    {
        _graphics.SetRenderTarget(null);
        _scene?.Dispose();
        _firstPass?.Dispose();
        _secondPass?.Dispose();
        _scene = null;
        _firstPass = null;
        _secondPass = null;
        _dimensions = default;
        HasCapturedFrame = false;
    }

    private void EnsureTargets(int width, int height)
    {
        var desired = FrostedFrameDimensions.Calculate(width, height);
        if (_scene is not null && !_scene.IsDisposed && desired == _dimensions) return;
        Release();
        _dimensions = desired;
        _scene = CreateTarget(width, height, RenderTargetUsage.PreserveContents);
        _firstPass = CreateTarget(desired.FirstPassWidth, desired.FirstPassHeight);
        _secondPass = CreateTarget(desired.SecondPassWidth, desired.SecondPassHeight);
    }

    private RenderTarget2D CreateTarget(
        int width,
        int height,
        RenderTargetUsage usage = RenderTargetUsage.DiscardContents) =>
        new(_graphics, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, usage);

    private void DrawTexture(Texture2D texture, Rectangle destination)
    {
        _batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp);
        _batch.Draw(texture, destination, Color.White);
        _batch.End();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Release();
        _batch.Dispose();
        _disposed = true;
    }
}
