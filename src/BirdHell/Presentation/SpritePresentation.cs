using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace BirdHell.Presentation;

/// <summary>SpriteBatch presentation entry points for screen-attached and future world-space sprites.</summary>
public sealed class SpritePresentation : IDisposable
{
    private static readonly Color BackdropColor = new(18, 27, 43);
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _generatedBackdrop;

    public SpritePresentation(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = new SpriteBatch(graphicsDevice);
        _generatedBackdrop = new Texture2D(graphicsDevice, 1, 1);
        _generatedBackdrop.SetData([BackdropColor]);
    }

    public void DrawBackground()
    {
        var viewport = _graphicsDevice.Viewport;
        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        _spriteBatch.Draw(_generatedBackdrop, viewport.Bounds, Color.White);
        _spriteBatch.End();
    }

    /// <summary>Begins a point-sampled world-space sprite layer using the same camera as primitive rendering.</summary>
    public void BeginWorldSprites(Camera2D camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            transformMatrix: CreateWorldTransform(camera, _graphicsDevice.Viewport));
    }

    public void EndWorldSprites() => _spriteBatch.End();

    public static Matrix CreateWorldTransform(Camera2D camera, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var view = camera.GetView(viewport.Width, viewport.Height);
        var pixelsPerWorldUnit = viewport.Height / view.VerticalSpan;
        return Matrix.CreateTranslation(-view.Center.X, -view.Center.Y, 0f) *
               Matrix.CreateScale(pixelsPerWorldUnit, -pixelsPerWorldUnit, 1f) *
               Matrix.CreateTranslation(viewport.Width / 2f, viewport.Height / 2f, 0f);
    }

    public static T LoadRequired<T>(ContentManager content, string assetName) where T : class
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        try
        {
            return content.Load<T>(assetName);
        }
        catch (ContentLoadException exception)
        {
            throw new InvalidOperationException(
                $"Could not load required presentation asset '{assetName}' from content root '{content.RootDirectory}'. " +
                "Confirm that the standalone-owned asset is included in the MonoGame content build.",
                exception);
        }
    }

    public void Dispose()
    {
        _generatedBackdrop.Dispose();
        _spriteBatch.Dispose();
    }
}
