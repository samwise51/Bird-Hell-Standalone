using System.Numerics;
using BirdHell.Core;
using BirdHell.Presentation;
using BirdHell.World.Geometry;

const float tolerance = 0.0001f;
const float pixelsPerWorldUnit = 72f;

var presentation = EnvironmentPresentationSettings.Default;
var expectedSky = NightSkyPalette.Default;
AssertVector3(expectedSky.Upper, Hex(0x10, 0x17, 0x2F), "Upper sky color changed.");
AssertVector3(expectedSky.Middle, Hex(0x29, 0x34, 0x5C), "Middle sky color changed.");
AssertVector3(expectedSky.Horizon, Hex(0x55, 0x47, 0x75), "Horizon color changed.");
AssertVector3(expectedSky.Haze, Hex(0x77, 0x72, 0x9B), "Haze color changed.");
Assert(presentation.SkyGrade.Brightness > 0.8f, "Night sky was excessively darkened.");
Console.WriteLine("PASS: centralized blue-violet sky palette and restrained grade are configured");

var cameraStart = new Vector2(2f, -3f);
var cameraEnd = new Vector2(12f, 5f);
foreach (var layer in presentation.Layers)
{
    var first = layer.Parallax.GetScreenTranslation(cameraStart, pixelsPerWorldUnit);
    var second = layer.Parallax.GetScreenTranslation(cameraEnd, pixelsPerWorldUnit);
    var expectedDelta = new Vector2(
        -(cameraEnd.X - cameraStart.X) * layer.Parallax.HorizontalFactor * pixelsPerWorldUnit,
        (cameraEnd.Y - cameraStart.Y) * layer.Parallax.VerticalFactor * pixelsPerWorldUnit);
    AssertVector2(second - first, expectedDelta, $"{layer.Layer} parallax translation is incorrect.");
    Assert(layer.Parallax.HorizontalFactor is > 0f and < 1f && layer.Parallax.VerticalFactor is > 0f and < 1f,
        $"{layer.Layer} parallax must remain behind gameplay movement.");
}
var expectedFactors = new Dictionary<EnvironmentSceneryLayer, (float X, float Y)>
{
    [EnvironmentSceneryLayer.HugeMountains] = (0.08f, 0.02f),
    [EnvironmentSceneryLayer.DistantCities] = (0.16f, 0.035f),
    [EnvironmentSceneryLayer.Ruins] = (0.30f, 0.06f),
    [EnvironmentSceneryLayer.NearRocks] = (0.50f, 0.10f),
    [EnvironmentSceneryLayer.RarePlants] = (0.50f, 0.10f),
};
foreach (var pair in expectedFactors)
{
    var layer = presentation.GetLayer(pair.Key).Parallax;
    AssertNear(layer.HorizontalFactor, pair.Value.X, $"{pair.Key} horizontal factor changed.");
    AssertNear(layer.VerticalFactor, pair.Value.Y, $"{pair.Key} vertical factor changed.");
}
Console.WriteLine("PASS: all authored layers use independent renderer-free parallax calculations");

var settings = CloudSettings.Default with { TextureWidth = 64, TextureHeight = 32, Octaves = 4 };
AssertNear(settings.Layer.HorizontalFactor, 0.04f, "Cloud horizontal depth changed.");
AssertNear(settings.Layer.VerticalFactor, 0.015f, "Cloud vertical depth changed.");
var cloudFirst = settings.Layer.GetScreenTranslation(cameraStart, pixelsPerWorldUnit);
var cloudSecond = settings.Layer.GetScreenTranslation(cameraEnd, pixelsPerWorldUnit);
AssertVector2(cloudSecond - cloudFirst, new Vector2(
        -(cameraEnd.X - cameraStart.X) * settings.Layer.HorizontalFactor * pixelsPerWorldUnit,
        (cameraEnd.Y - cameraStart.Y) * settings.Layer.VerticalFactor * pixelsPerWorldUnit),
    "Cloud parallax translation is incorrect.");
Console.WriteLine("PASS: clouds retain their own very distant parallax depth");

var firstMountainSeed = EnvironmentSeedDerivation.Derive("layout-seed", "m9.huge-mountains.v1");
Assert(firstMountainSeed == EnvironmentSeedDerivation.Derive("layout-seed", "m9.huge-mountains.v1"),
    "Named layer seed derivation was unstable.");
Assert(firstMountainSeed != EnvironmentSeedDerivation.Derive("layout-seed", "m9.ruins.v1"),
    "Different named layers received the same sub-seed.");
Assert(firstMountainSeed != EnvironmentSeedDerivation.Derive("other-layout-seed", "m9.huge-mountains.v1"),
    "Different run seeds received the same layer sub-seed.");
Console.WriteLine("PASS: stable versioned per-layer sub-seeds are independent");

const float worldMin = -65f;
const float worldMax = 65f;
var layoutA = EnvironmentLayoutGenerator.Generate("deterministic-layout", worldMin, worldMax, presentation);
var layoutB = EnvironmentLayoutGenerator.Generate("deterministic-layout", worldMin, worldMax, presentation);
var layoutOther = EnvironmentLayoutGenerator.Generate("different-layout", worldMin, worldMax, presentation);
Assert(LayoutFingerprint(layoutA) == LayoutFingerprint(layoutB), "Same-seed scenery layout changed.");
Assert(LayoutFingerprint(layoutA) != LayoutFingerprint(layoutOther), "Different run seeds produced identical layouts.");
Console.WriteLine("PASS: static environment layout is deterministic per run seed and varies for a new seed");

var changedPlantSettings = new EnvironmentPresentationSettings
{
    Sky = presentation.Sky,
    SkyGrade = presentation.SkyGrade,
    HorizonGlowStrength = presentation.HorizonGlowStrength,
    Layers = presentation.Layers.Select(layer => layer.Layer == EnvironmentSceneryLayer.RarePlants
        ? layer with { MinimumSpacing = 32f, MaximumSpacing = 40f, SeedName = "m9.rare-plants.v2" }
        : layer).ToArray(),
};
var layoutWithChangedPlants = EnvironmentLayoutGenerator.Generate("deterministic-layout", worldMin, worldMax,
    changedPlantSettings);
foreach (var layer in Enum.GetValues<EnvironmentSceneryLayer>().Where(layer => layer != EnvironmentSceneryLayer.RarePlants))
    Assert(LayerFingerprint(layoutA[layer]) == LayerFingerprint(layoutWithChangedPlants[layer]),
        $"Changing plants perturbed unrelated layer {layer}.");
Assert(LayerFingerprint(layoutA[EnvironmentSceneryLayer.RarePlants]) !=
       LayerFingerprint(layoutWithChangedPlants[EnvironmentSceneryLayer.RarePlants]),
    "Changed plant layer did not produce a changed plant layout.");
Console.WriteLine("PASS: adding or versioning one visual layer does not perturb unrelated layers");

var mountains = presentation.GetLayer(EnvironmentSceneryLayer.HugeMountains);
var requiredCoverage = EnvironmentLayoutGenerator.GetRequiredCoverage(worldMin, worldMax, mountains);
Assert(EnvironmentLayoutGenerator.HasContinuousCoverage(layoutA[EnvironmentSceneryLayer.HugeMountains],
        requiredCoverage.Minimum, requiredCoverage.Maximum),
    "Mountain layout does not continuously cover its overscanned range.");
foreach (var aspect in new[] { 4f / 3f, 16f / 9f, 21f / 9f })
foreach (var cameraX in new[] { worldMin, 0f, worldMax })
{
    var horizontalSpan = Camera2D.VerticalViewSpan * aspect;
    var visibleMinimum = cameraX * mountains.Parallax.HorizontalFactor - horizontalSpan / 2f;
    var visibleMaximum = cameraX * mountains.Parallax.HorizontalFactor + horizontalSpan / 2f;
    Assert(EnvironmentLayoutGenerator.HasContinuousCoverage(layoutA[EnvironmentSceneryLayer.HugeMountains],
            visibleMinimum, visibleMaximum),
        $"Mountains exposed a gap at camera {cameraX} and aspect {aspect}.");
}
Assert(layoutA[EnvironmentSceneryLayer.RarePlants].Count < layoutA[EnvironmentSceneryLayer.NearRocks].Count,
    "Rare plants were not sparser than rocks.");
Console.WriteLine("PASS: mountain overscan covers representative cameras/aspects and vegetation remains sparse");

var catalog = AlienDesertAssetCatalog.All;
Assert(catalog.Count == 16 && catalog.Select(asset => asset.TextureId).Distinct(StringComparer.Ordinal).Count() == 16,
    "Environment asset catalog is incomplete or contains duplicates.");
var repositoryRoot = FindRepositoryRoot();
foreach (var asset in catalog)
{
    var path = Path.Combine(repositoryRoot, "src", "BirdHell", "Content",
        asset.TextureId.Replace('/', Path.DirectorySeparatorChar));
    Assert(File.Exists(path), $"Catalog asset is missing: {asset.TextureId}");
}
Assert(catalog.All(asset => !asset.TextureId.Contains("ExtractionTests", StringComparison.Ordinal) &&
                            !asset.TextureId.EndsWith("alien_desert_asset_sheet.png", StringComparison.Ordinal)),
    "Reference/QC imagery leaked into the runtime catalog.");
Console.WriteLine("PASS: all 16 final RGBA assets exist and reference/QC imagery is excluded from the catalog");

var generatedA = CloudFieldGenerator.Generate(settings, 0);
var generatedB = CloudFieldGenerator.Generate(settings, 0);
var generatedOtherSeed = CloudFieldGenerator.Generate(settings with { VisualSeed = settings.VisualSeed + 1u }, 0);
Assert(CloudFieldGenerator.Fingerprint(generatedA) == CloudFieldGenerator.Fingerprint(generatedB),
    "Same cloud seed/configuration did not reproduce texture data.");
Assert(CloudFieldGenerator.Fingerprint(generatedA) != CloudFieldGenerator.Fingerprint(generatedOtherSeed),
    "Different cloud seeds produced identical texture data.");
Assert(generatedA.Any(pixel => pixel.A > 0) && generatedA.Any(pixel => pixel.A == 0),
    "Cloud field lacks both wisps and clear sky.");

var atmosphere = new CloudAtmosphereState();
atmosphere.Advance(9.5d);
atmosphere.ToggleVisibility();
var elapsedBeforeReset = atmosphere.ElapsedSeconds;
var visibleBeforeReset = atmosphere.IsVisible;
var run = new MainScreenRun("environment-reset-policy");
var sameSeedLayout = LayoutFingerprint(EnvironmentLayoutGenerator.Generate(run.Seed,
    run.Geometry.MinX, run.Geometry.MaxX));
run.RestartSameSeed();
atmosphere.OnGameplayReset();
Assert(LayoutFingerprint(EnvironmentLayoutGenerator.Generate(run.Seed, run.Geometry.MinX, run.Geometry.MaxX)) ==
       sameSeedLayout, "F5/same-seed reset changed static environment layout.");
Assert(atmosphere.ElapsedSeconds == elapsedBeforeReset && atmosphere.IsVisible == visibleBeforeReset,
    "F5/same-seed reset changed cloud clock or visibility.");
var oldSeed = run.Seed;
run.StartNewRun();
atmosphere.OnGameplayReset();
Assert(run.Seed != oldSeed && LayoutFingerprint(EnvironmentLayoutGenerator.Generate(run.Seed,
        run.Geometry.MinX, run.Geometry.MaxX)) != sameSeedLayout,
    "R/new-seed reset did not change static environment layout.");
Assert(atmosphere.ElapsedSeconds == elapsedBeforeReset && atmosphere.IsVisible == visibleBeforeReset,
    "R/new-seed reset changed cloud clock or visibility.");
Console.WriteLine("PASS: F5 reproduces scenery, R changes scenery, and both preserve cloud continuity");

var controlRun = new MainScreenRun("environment-rng-isolation");
var controlGeometry = GeometryFingerprint(controlRun.Geometry);
_ = EnvironmentLayoutGenerator.Generate(controlRun.Seed, controlRun.Geometry.MinX, controlRun.Geometry.MaxX);
_ = CloudFieldGenerator.Generate(settings with { VisualSeed = 123456u }, 1);
var runAfterVisualGeneration = new MainScreenRun("environment-rng-isolation");
Assert(controlGeometry == GeometryFingerprint(runAfterVisualGeneration.Geometry),
    "Environment generation consumed or altered gameplay cave RNG behavior.");
Console.WriteLine("PASS: environment RNG is isolated from gameplay generation");

PresentationLayer[] expectedOrder =
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
Assert(PresentationRenderOrder.Layers.SequenceEqual(expectedOrder), "Logical render order is incorrect.");
Assert(Array.IndexOf(expectedOrder, PresentationLayer.CloudAtmosphere) <
       Array.IndexOf(expectedOrder, PresentationLayer.CaveTerrain), "Clouds do not remain behind cave terrain.");
Assert(Array.IndexOf(expectedOrder, PresentationLayer.NearBackground) <
       Array.IndexOf(expectedOrder, PresentationLayer.Player), "Decorative scenery can occlude the player.");
Console.WriteLine("PASS: explicit render ordering keeps all scenery and clouds behind gameplay");
Console.WriteLine("All Milestone 9 environment presentation checks passed.");

static Vector3 Hex(byte red, byte green, byte blue) => new(red / 255f, green / 255f, blue / 255f);

static string LayoutFingerprint(EnvironmentLayout layout) => string.Join("||",
    Enum.GetValues<EnvironmentSceneryLayer>().Select(layer => $"{layer}:{LayerFingerprint(layout[layer])}"));

static string LayerFingerprint(IEnumerable<SceneryInstance> instances) => string.Join('|', instances.Select(instance =>
    $"{instance.TextureId},{instance.Position.X:R},{instance.Position.Y:R},{instance.Scale:R},{instance.Opacity:R},{instance.FlipHorizontally}"));

static string GeometryFingerprint(CaveGeometry geometry) => string.Join('|',
    geometry.Vertices.Select(vertex => $"{vertex.X:R},{vertex.Y:R}")) + ":" +
    string.Join('|', geometry.Triangles.Select(triangle => $"{triangle.A},{triangle.B},{triangle.C}"));

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BirdHell.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new InvalidOperationException("Could not locate repository root for asset checks.");
}

static void AssertNear(float actual, float expected, string message)
{
    if (MathF.Abs(actual - expected) > tolerance)
        throw new InvalidOperationException($"{message} Expected {expected:R}, got {actual:R}.");
}

static void AssertVector3(Vector3 actual, Vector3 expected, string message)
{
    if (Vector3.Distance(actual, expected) > tolerance)
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void AssertVector2(Vector2 actual, Vector2 expected, string message)
{
    if (Vector2.Distance(actual, expected) > tolerance)
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
