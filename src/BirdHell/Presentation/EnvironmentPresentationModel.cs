using System.Numerics;

namespace BirdHell.Presentation;

/// <summary>Renderer-independent configuration for one screen-composited parallax layer.</summary>
public readonly record struct ParallaxLayerSettings(
    float HorizontalFactor,
    float VerticalFactor,
    float Scale,
    Vector2 WorldOffset,
    Vector2 ScreenOffset,
    SpriteTextureFilter Filter)
{
    public Vector2 GetScreenTranslation(Vector2 cameraPosition, float pixelsPerWorldUnit) =>
        new(
            ScreenOffset.X + (WorldOffset.X - cameraPosition.X * HorizontalFactor) * pixelsPerWorldUnit,
            ScreenOffset.Y - (WorldOffset.Y - cameraPosition.Y * VerticalFactor) * pixelsPerWorldUnit);

    public Vector2 GetScreenPosition(Vector2 worldPosition, Vector2 cameraPosition, float pixelsPerWorldUnit) =>
        GetScreenTranslation(cameraPosition, pixelsPerWorldUnit) +
        new Vector2(worldPosition.X * pixelsPerWorldUnit, -worldPosition.Y * pixelsPerWorldUnit);
}

public readonly record struct BackgroundColorGrade(float Brightness, float Contrast, Vector3 Tint)
{
    public static BackgroundColorGrade Default => new(0.92f, 0.96f, new Vector3(0.96f, 0.97f, 1f));

    public void Validate()
    {
        if (!float.IsFinite(Brightness) || Brightness is < 0f or > 2f) throw new ArgumentOutOfRangeException(nameof(Brightness));
        if (!float.IsFinite(Contrast) || Contrast is < 0f or > 2f) throw new ArgumentOutOfRangeException(nameof(Contrast));
        if (!IsUnit(Tint.X) || !IsUnit(Tint.Y) || !IsUnit(Tint.Z)) throw new ArgumentOutOfRangeException(nameof(Tint));
    }

    public Vector3 Apply(Vector3 color)
    {
        Validate();
        var contrasted = (color - new Vector3(0.5f)) * Contrast + new Vector3(0.5f);
        return Vector3.Clamp(contrasted * Brightness * Tint, Vector3.Zero, Vector3.One);
    }

    private static bool IsUnit(float value) => float.IsFinite(value) && value is >= 0f and <= 1f;
}

public readonly record struct NightSkyPalette(Vector3 Upper, Vector3 Middle, Vector3 Horizon, Vector3 Haze)
{
    public static NightSkyPalette Default => new(
        Hex(0x10, 0x17, 0x2F),
        Hex(0x29, 0x34, 0x5C),
        Hex(0x55, 0x47, 0x75),
        Hex(0x77, 0x72, 0x9B));

    private static Vector3 Hex(byte red, byte green, byte blue) => new(red / 255f, green / 255f, blue / 255f);
}

public enum EnvironmentSceneryLayer
{
    HugeMountains,
    DistantCities,
    Ruins,
    NearRocks,
    RarePlants,
}

public sealed record SceneryLayerSettings(
    EnvironmentSceneryLayer Layer,
    string SeedName,
    ParallaxLayerSettings Parallax,
    Vector3 Tint,
    float Opacity,
    float Brightness,
    float BaselineY,
    float BaseHeight,
    float MinimumScale,
    float MaximumScale,
    float MinimumSpacing,
    float MaximumSpacing,
    float Overscan,
    bool RequiresContinuousCoverage);

public readonly record struct SceneryAssetDefinition(
    string TextureId,
    int PixelWidth,
    int PixelHeight,
    bool AllowHorizontalFlip,
    float Weight = 1f)
{
    public float AspectRatio => PixelWidth / (float)PixelHeight;
}

public sealed class EnvironmentPresentationSettings
{
    public static EnvironmentPresentationSettings Default { get; } = new();

    public NightSkyPalette Sky { get; init; } = NightSkyPalette.Default;
    public BackgroundColorGrade SkyGrade { get; init; } = BackgroundColorGrade.Default;
    public float HorizonGlowStrength { get; init; } = 0.34f;
    public IReadOnlyList<SceneryLayerSettings> Layers { get; init; } = CreateLayers();

    public SceneryLayerSettings GetLayer(EnvironmentSceneryLayer layer) =>
        Layers.Single(settings => settings.Layer == layer);

    private static IReadOnlyList<SceneryLayerSettings> CreateLayers() =>
    [
        new(EnvironmentSceneryLayer.HugeMountains, "m9.huge-mountains.v1",
            new ParallaxLayerSettings(0.08f, 0.02f, 1f, Vector2.Zero, Vector2.Zero, SpriteTextureFilter.Linear),
            new Vector3(0.78f, 0.84f, 1f), 0.90f, 0.92f, -3.35f, 5.7f, 0.88f, 1.12f,
            0.20f, 0.34f, 8f, true),
        new(EnvironmentSceneryLayer.DistantCities, "m9.distant-cities.v1",
            new ParallaxLayerSettings(0.16f, 0.035f, 1f, Vector2.Zero, Vector2.Zero, SpriteTextureFilter.Linear),
            new Vector3(0.88f, 0.90f, 1f), 0.86f, 0.98f, -3.55f, 3.35f, 0.82f, 1.08f,
            12f, 19f, 9f, false),
        new(EnvironmentSceneryLayer.Ruins, "m9.ruins.v1",
            new ParallaxLayerSettings(0.30f, 0.06f, 1f, Vector2.Zero, Vector2.Zero, SpriteTextureFilter.Linear),
            new Vector3(0.92f, 0.91f, 1f), 0.90f, 1.02f, -3.80f, 2.75f, 0.84f, 1.15f,
            10f, 16f, 10f, false),
        new(EnvironmentSceneryLayer.NearRocks, "m9.near-rocks.v1",
            new ParallaxLayerSettings(0.50f, 0.10f, 1f, Vector2.Zero, Vector2.Zero, SpriteTextureFilter.Linear),
            new Vector3(0.96f, 0.94f, 1f), 0.93f, 1.04f, -4.20f, 1.55f, 0.76f, 1.20f,
            4.5f, 7.5f, 12f, false),
        new(EnvironmentSceneryLayer.RarePlants, "m9.rare-plants.v1",
            new ParallaxLayerSettings(0.50f, 0.10f, 1f, Vector2.Zero, Vector2.Zero, SpriteTextureFilter.Linear),
            Vector3.One, 0.94f, 1.06f, -4.18f, 2.30f, 0.82f, 1.08f,
            20f, 31f, 14f, false),
    ];
}

public static class AlienDesertAssetCatalog
{
    public static IReadOnlyDictionary<EnvironmentSceneryLayer, IReadOnlyList<SceneryAssetDefinition>> ByLayer { get; } =
        new Dictionary<EnvironmentSceneryLayer, IReadOnlyList<SceneryAssetDefinition>>
        {
            [EnvironmentSceneryLayer.HugeMountains] =
            [
                new("Backgrounds/Mountains/mountain_01.png", 2106, 500, false),
                new("Backgrounds/Mountains/mountain_02.png", 1924, 657, false),
                new("Backgrounds/Mountains/mountain_03.png", 2167, 590, false),
            ],
            [EnvironmentSceneryLayer.DistantCities] =
            [
                new("Backgrounds/Cities/city_01.png", 2006, 701, false),
                new("Backgrounds/Cities/city_02.png", 2017, 605, false),
                new("Backgrounds/Cities/city_03.png", 2162, 640, false),
            ],
            [EnvironmentSceneryLayer.Ruins] =
            [
                new("Backgrounds/Ruins/ruin_01.png", 2156, 701, false),
                new("Backgrounds/Ruins/ruin_02.png", 2170, 661, false),
                new("Backgrounds/Ruins/ruin_03.png", 2051, 680, false),
            ],
            [EnvironmentSceneryLayer.NearRocks] =
            [
                new("Backgrounds/Rocks/rock_01.png", 1941, 570, true),
                new("Backgrounds/Rocks/rock_02.png", 1494, 466, true),
                new("Backgrounds/Rocks/rock_03.png", 2155, 693, true),
                new("Backgrounds/Rocks/rock_04.png", 1418, 824, true),
                new("Backgrounds/Rocks/rock_05.png", 2130, 619, true),
            ],
            [EnvironmentSceneryLayer.RarePlants] =
            [
                new("Backgrounds/Plants/alien_plant_01.png", 1062, 1357, true),
                new("Backgrounds/Plants/alien_plant_02.png", 1665, 708, true),
            ],
        };

    public static IReadOnlyList<SceneryAssetDefinition> All { get; } =
        ByLayer.Values.SelectMany(assets => assets).ToArray();
}

public enum PresentationLayer
{
    NightSky,
    CloudAtmosphere,
    HugeMountains,
    DistantCities,
    Ruins,
    NearBackground,
    CaveTerrain,
    Enemies,
    Player,
    Projectiles,
    FutureEffects,
    DebugOverlays,
}

public static class PresentationRenderOrder
{
    public static IReadOnlyList<PresentationLayer> Layers { get; } =
    [
        PresentationLayer.NightSky,
        PresentationLayer.CloudAtmosphere,
        PresentationLayer.HugeMountains,
        PresentationLayer.DistantCities,
        PresentationLayer.Ruins,
        PresentationLayer.NearBackground,
        PresentationLayer.CaveTerrain,
        PresentationLayer.Enemies,
        PresentationLayer.Player,
        PresentationLayer.Projectiles,
        PresentationLayer.FutureEffects,
        PresentationLayer.DebugOverlays,
    ];
}
