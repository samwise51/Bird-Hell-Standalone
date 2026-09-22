using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace BirdHell.Presentation;

/// <summary>Night sky, authored deterministic scenery, and runtime-generated atmosphere.</summary>
public sealed class EnvironmentPresentation : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly Texture2D _skyGradient;
    private readonly Dictionary<string, Texture2D> _sceneryTextures = new(StringComparer.Ordinal);
    private readonly Texture2D[] _cloudFrames = new Texture2D[CloudFieldGenerator.TemporalFrameCount];
    private readonly CloudSettings _cloudSettings;
    private readonly EnvironmentPresentationSettings _settings;
    private EnvironmentLayout? _layout;

    public EnvironmentPresentation(GraphicsDevice graphicsDevice, string contentRoot,
        CloudSettings? cloudSettings = null, EnvironmentPresentationSettings? settings = null)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        _spriteBatch = new SpriteBatch(graphicsDevice);
        _cloudSettings = cloudSettings ?? CloudSettings.Default;
        _cloudSettings.Validate();
        _settings = settings ?? EnvironmentPresentationSettings.Default;
        _settings.SkyGrade.Validate();
        _skyGradient = CreateSkyGradient(_settings);

        foreach (var asset in AlienDesertAssetCatalog.All)
            _sceneryTextures.Add(asset.TextureId, LoadRequiredTexture(contentRoot, asset.TextureId));

        for (var index = 0; index < _cloudFrames.Length; index++)
        {
            var texture = new Texture2D(graphicsDevice, _cloudSettings.TextureWidth, _cloudSettings.TextureHeight,
                false, SurfaceFormat.Color);
            texture.SetData(CloudFieldGenerator.Generate(_cloudSettings, index));
            _cloudFrames[index] = texture;
        }
    }

    public EnvironmentLayout? Layout => _layout;

    public void SetRunLayout(string runSeed, float worldMinX, float worldMaxX) =>
        _layout = EnvironmentLayoutGenerator.Generate(runSeed, worldMinX, worldMaxX, _settings);

    public void DrawSky()
    {
        var viewport = _graphicsDevice.Viewport;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        _spriteBatch.Draw(_skyGradient, new Rectangle(0, 0, viewport.Width, viewport.Height), Color.White);
        _spriteBatch.End();
    }

    public void DrawClouds(Camera2D camera, CloudAtmosphereState state)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsVisible) return;
        var viewport = _graphicsDevice.Viewport;
        var pixelsPerWorldUnit = viewport.Height / Camera2D.VerticalViewSpan;
        var translation = _cloudSettings.Layer.GetScreenTranslation(camera.Position, pixelsPerWorldUnit);
        var motion = state.GetMotion(_cloudSettings);
        var destinationHeight = Math.Max(1,
            (int)MathF.Ceiling(viewport.Height * _cloudSettings.VisualScale * _cloudSettings.Layer.Scale));
        var destinationWidth = Math.Max(1,
            (int)MathF.Ceiling(destinationHeight * (_cloudSettings.TextureWidth / (float)_cloudSettings.TextureHeight)));
        var centerX = viewport.Width / 2f + translation.X + motion.Drift.X * destinationWidth;
        var centerY = viewport.Height / 2f + translation.Y - motion.Drift.Y * destinationHeight;
        var firstTint = Color.White * (_cloudSettings.Opacity * (1f - motion.Blend));
        var secondTint = Color.White * (_cloudSettings.Opacity * motion.Blend);

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        DrawCoveringTiles(_cloudFrames[motion.FirstFrame], centerX, centerY, destinationWidth, destinationHeight, firstTint);
        if (motion.Blend > 0f)
            DrawCoveringTiles(_cloudFrames[motion.SecondFrame], centerX, centerY, destinationWidth, destinationHeight, secondTint);
        _spriteBatch.End();
    }

    public void DrawSceneryLayer(Camera2D camera, EnvironmentSceneryLayer layer)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (_layout is null) return;
        var layerSettings = _settings.GetLayer(layer);
        var viewport = _graphicsDevice.Viewport;
        var pixelsPerWorldUnit = viewport.Height / Camera2D.VerticalViewSpan;
        var sampler = layerSettings.Parallax.Filter == SpriteTextureFilter.Linear
            ? SamplerState.LinearClamp
            : SamplerState.PointClamp;
        var tintVector = Vector3.Clamp(layerSettings.Tint * layerSettings.Brightness, Vector3.Zero, Vector3.One);

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, sampler,
            DepthStencilState.None, RasterizerState.CullNone);
        foreach (var instance in _layout[layer])
        {
            var baseline = layerSettings.Parallax.GetScreenPosition(instance.Position, camera.Position, pixelsPerWorldUnit);
            baseline += new Vector2(viewport.Width / 2f, viewport.Height / 2f);
            var width = Math.Max(1, (int)MathF.Ceiling(instance.WorldWidth * pixelsPerWorldUnit));
            var height = Math.Max(1, (int)MathF.Ceiling(instance.WorldHeight * pixelsPerWorldUnit));
            var destination = new Rectangle(
                (int)MathF.Floor(baseline.X - width / 2f),
                (int)MathF.Floor(baseline.Y - height),
                width,
                height);
            const int cullingMargin = 24;
            if (destination.Right < -cullingMargin || destination.Left > viewport.Width + cullingMargin ||
                destination.Bottom < -cullingMargin || destination.Top > viewport.Height + cullingMargin)
                continue;
            var tint = Color.FromNonPremultiplied(
                (byte)MathF.Round(tintVector.X * 255f),
                (byte)MathF.Round(tintVector.Y * 255f),
                (byte)MathF.Round(tintVector.Z * 255f),
                255) * (layerSettings.Opacity * instance.Opacity);
            _spriteBatch.Draw(_sceneryTextures[instance.TextureId], destination, null, tint, 0f,
                Microsoft.Xna.Framework.Vector2.Zero,
                instance.FlipHorizontally ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }
        _spriteBatch.End();
    }

    private Texture2D CreateSkyGradient(EnvironmentPresentationSettings settings)
    {
        const int width = 8;
        const int height = 256;
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            var amount = y / (height - 1f);
            Vector3 sky;
            if (amount < 0.54f)
                sky = Vector3.Lerp(settings.Sky.Upper, settings.Sky.Middle, Smooth(amount / 0.54f));
            else if (amount < 0.88f)
                sky = Vector3.Lerp(settings.Sky.Middle, settings.Sky.Horizon, Smooth((amount - 0.54f) / 0.34f));
            else
            {
                var restrainedHaze = Vector3.Lerp(settings.Sky.Horizon, settings.Sky.Haze,
                    settings.HorizonGlowStrength);
                sky = Vector3.Lerp(settings.Sky.Horizon, restrainedHaze, Smooth((amount - 0.88f) / 0.12f));
            }
            var graded = settings.SkyGrade.Apply(sky);
            var color = new Color(
                (byte)MathF.Round(graded.X * 255f),
                (byte)MathF.Round(graded.Y * 255f),
                (byte)MathF.Round(graded.Z * 255f));
            for (var x = 0; x < width; x++) pixels[y * width + x] = color;
        }
        var texture = new Texture2D(_graphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        return texture;
    }

    private static float Smooth(float value) => value * value * (3f - 2f * value);

    private Texture2D LoadRequiredTexture(string contentRoot, string textureId)
    {
        var path = Path.Combine(contentRoot, textureId.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            using var stream = File.OpenRead(path);
            // Texture2D.FromStream leaves PNG data in straight-alpha form. SpriteBatch's
            // AlphaBlend state expects premultiplied color, so convert at upload time to
            // prevent low-alpha edge RGB from being added as bright fringe pixels.
            return Texture2D.FromStream(_graphicsDevice, stream, DefaultColorProcessors.PremultiplyAlpha);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new InvalidOperationException($"Could not load environment texture '{textureId}' at '{path}'.", exception);
        }
    }

    private void DrawCoveringTiles(Texture2D texture, float centerX, float centerY, int width, int height, Color tint)
    {
        var firstY = centerY;
        while (firstY - height / 2f > 0f) firstY -= height;
        while (firstY + height / 2f < 0f) firstY += height;
        for (var y = firstY; y - height / 2f < _graphicsDevice.Viewport.Height; y += height)
        {
            var firstX = centerX;
            while (firstX - width / 2f > 0f) firstX -= width;
            while (firstX + width / 2f < 0f) firstX += width;
            for (var x = firstX; x - width / 2f < _graphicsDevice.Viewport.Width; x += width)
                _spriteBatch.Draw(texture, CenteredRectangle(x, y, width, height), tint);
        }
    }

    private static Rectangle CenteredRectangle(float x, float y, int width, int height) => new(
        (int)MathF.Floor(x - width / 2f), (int)MathF.Floor(y - height / 2f), width, height);

    public void Dispose()
    {
        _skyGradient.Dispose();
        foreach (var texture in _sceneryTextures.Values) texture.Dispose();
        foreach (var cloudFrame in _cloudFrames) cloudFrame.Dispose();
        _spriteBatch.Dispose();
    }
}
