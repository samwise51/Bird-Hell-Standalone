using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BirdHell.Presentation;

/// <summary>SpriteBatch presentation entry points for filtered world-space gameplay sprites.</summary>
public sealed class SpritePresentation : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private SpriteTextureFilter? _activeWorldFilter;

    public SpritePresentation(GraphicsDevice graphicsDevice, string contentRoot)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _spriteBatch = new SpriteBatch(graphicsDevice);
        var requiredTextures = BirdHellVisuals.PlayerClips.SelectMany(clip => clip.Frames)
            .Concat(BirdHellVisuals.Enemy.Frames)
            .Concat(BirdHellVisuals.Projectile.Frames)
            .Select(frame => frame.Region.TextureId)
            .Distinct(StringComparer.Ordinal);
        foreach (var textureId in requiredTextures) _textures.Add(textureId, LoadRequiredTexture(contentRoot, textureId));
    }

    /// <summary>Begins a per-asset-filtered world sprite layer using the primitive-rendering camera.</summary>
    public void BeginWorldSprites(Camera2D camera, SpriteTextureFilter filter, bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _activeWorldFilter = filter;
        _spriteBatch.Begin(
            SpriteSortMode.Deferred,
            additive ? BlendState.Additive : BlendState.AlphaBlend,
            filter == SpriteTextureFilter.Point ? SamplerState.PointClamp : SamplerState.LinearClamp,
            DepthStencilState.None,
            RasterizerState.CullNone,
            transformMatrix: CreateWorldTransform(camera, _graphicsDevice.Viewport));
    }

    public void EndWorldSprites()
    {
        _spriteBatch.End();
        _activeWorldFilter = null;
    }

    public void Draw(SpriteFrame frame, System.Numerics.Vector2 worldPosition, SpriteFlip flip, Color tint)
    {
        var region = frame.Region;
        if (_activeWorldFilter != region.Filter)
            throw new InvalidOperationException(
                $"Sprite '{region.TextureId}' requires {region.Filter} filtering but the active world layer uses {_activeWorldFilter?.ToString() ?? "none"}.");
        var texture = _textures[region.TextureId];
        var sourceY = region.CoordinateOrigin == PixelCoordinateOrigin.BottomLeft
            ? texture.Height - region.Source.Y - region.Source.Height
            : region.Source.Y;
        var source = new Rectangle(region.Source.X, sourceY, region.Source.Width, region.Source.Height);
        // SpriteBatch's local Y grows down, while the world transform makes Y grow up. Flipping
        // texture coordinates vertically preserves the PNG orientation; Unity pivot Y can then be
        // used directly. A signed X scale mirrors around the pivot, including bottom-left slashes.
        var origin = new Vector2(region.Pivot.X * source.Width, region.Pivot.Y * source.Height);
        var scale = region.WorldScale / region.PixelsPerUnit;
        _spriteBatch.Draw(
            texture,
            new Vector2(worldPosition.X, worldPosition.Y),
            source,
            tint,
            0f,
            origin,
            new Vector2(flip == SpriteFlip.Horizontal ? -scale : scale, scale),
            SpriteEffects.FlipVertically,
            0f);
    }

    public static Matrix CreateWorldTransform(Camera2D camera, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var view = camera.GetView(viewport.Width, viewport.Height);
        var pixelsPerWorldUnit = viewport.Height / view.VerticalSpan;
        return Matrix.CreateTranslation(-view.Center.X, -view.Center.Y, 0f) *
               Matrix.CreateScale(pixelsPerWorldUnit, -pixelsPerWorldUnit, 1f) *
               Matrix.CreateTranslation(viewport.Width / 2f, viewport.Height / 2f, 0f);
    }

    private Texture2D LoadRequiredTexture(string contentRoot, string textureId)
    {
        var path = Path.Combine(contentRoot, textureId.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            using var stream = File.OpenRead(path);
            if (RequiresPremultipliedAlphaUpload(textureId))
            {
                // The generated runtime atlases are conventional straight-alpha PNGs. SpriteBatch's
                // AlphaBlend state expects premultiplied color, so convert during upload;
                // otherwise bilinear sampling adds low-alpha edge RGB as a light halo.
                return Texture2D.FromStream(_graphicsDevice, stream, DefaultColorProcessors.PremultiplyAlpha);
            }
            return Texture2D.FromStream(_graphicsDevice, stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Could not load required presentation texture '{textureId}' at '{path}'. " +
                "Confirm that the standalone-owned PNG is present and copied to the output Content directory.",
                exception);
        }
    }

    public static bool RequiresPremultipliedAlphaUpload(string textureId) =>
        textureId == BirdHellVisuals.PlayerTexture || textureId == BirdHellVisuals.EnemyTexture;

    public void Dispose()
    {
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        _spriteBatch.Dispose();
    }
}
