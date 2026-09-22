using System.Numerics;

namespace BirdHell.Presentation;

public readonly record struct SceneryInstance(
    string TextureId,
    Vector2 Position,
    float Scale,
    float Opacity,
    bool FlipHorizontally,
    float WorldWidth,
    float WorldHeight);

public sealed class EnvironmentLayout
{
    public EnvironmentLayout(string runSeed,
        IReadOnlyDictionary<EnvironmentSceneryLayer, IReadOnlyList<SceneryInstance>> layers)
    {
        RunSeed = runSeed;
        Layers = layers;
    }

    public string RunSeed { get; }
    public IReadOnlyDictionary<EnvironmentSceneryLayer, IReadOnlyList<SceneryInstance>> Layers { get; }
    public IReadOnlyList<SceneryInstance> this[EnvironmentSceneryLayer layer] => Layers[layer];
}

public static class EnvironmentSeedDerivation
{
    public static ulong Derive(string runSeed, string layerName)
    {
        ArgumentNullException.ThrowIfNull(runSeed);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerName);
        const ulong offset = 14695981039346656037ul;
        const ulong prime = 1099511628211ul;
        var hash = offset;
        foreach (var character in runSeed)
        {
            hash = (hash ^ (byte)character) * prime;
            hash = (hash ^ (byte)(character >> 8)) * prime;
        }
        hash = (hash ^ 0xFFu) * prime;
        foreach (var character in layerName)
        {
            hash = (hash ^ (byte)character) * prime;
            hash = (hash ^ (byte)(character >> 8)) * prime;
        }
        return hash;
    }
}

public static class EnvironmentLayoutGenerator
{
    public const float RepresentativeMaximumHorizontalSpan = 24f;

    public static EnvironmentLayout Generate(string runSeed, float worldMinX, float worldMaxX,
        EnvironmentPresentationSettings? presentationSettings = null)
    {
        ArgumentNullException.ThrowIfNull(runSeed);
        if (!float.IsFinite(worldMinX) || !float.IsFinite(worldMaxX) || worldMaxX <= worldMinX)
            throw new ArgumentOutOfRangeException(nameof(worldMaxX));
        var settings = presentationSettings ?? EnvironmentPresentationSettings.Default;
        var result = new Dictionary<EnvironmentSceneryLayer, IReadOnlyList<SceneryInstance>>();
        foreach (var layer in settings.Layers)
            result[layer.Layer] = GenerateLayer(runSeed, worldMinX, worldMaxX, layer,
                AlienDesertAssetCatalog.ByLayer[layer.Layer]);
        return new EnvironmentLayout(runSeed, result);
    }

    public static (float Minimum, float Maximum) GetRequiredCoverage(float worldMinX, float worldMaxX,
        SceneryLayerSettings settings, float horizontalSpan = RepresentativeMaximumHorizontalSpan)
    {
        var halfSpan = horizontalSpan / 2f;
        return (worldMinX * settings.Parallax.HorizontalFactor - halfSpan - settings.Overscan,
            worldMaxX * settings.Parallax.HorizontalFactor + halfSpan + settings.Overscan);
    }

    public static bool HasContinuousCoverage(IReadOnlyList<SceneryInstance> instances, float minimum, float maximum)
    {
        var intervals = instances.Select(instance =>
                (Left: instance.Position.X - instance.WorldWidth / 2f, Right: instance.Position.X + instance.WorldWidth / 2f))
            .OrderBy(interval => interval.Left).ToArray();
        if (intervals.Length == 0 || intervals[0].Left > minimum) return false;
        var covered = intervals[0].Right;
        foreach (var interval in intervals.Skip(1))
        {
            if (interval.Left > covered) return false;
            covered = Math.Max(covered, interval.Right);
            if (covered >= maximum) return true;
        }
        return covered >= maximum;
    }

    private static IReadOnlyList<SceneryInstance> GenerateLayer(string runSeed, float worldMinX, float worldMaxX,
        SceneryLayerSettings settings, IReadOnlyList<SceneryAssetDefinition> assets)
    {
        var random = new StableVisualRandom(EnvironmentSeedDerivation.Derive(runSeed, settings.SeedName));
        var coverage = GetRequiredCoverage(worldMinX, worldMaxX, settings);
        var instances = new List<SceneryInstance>();
        if (settings.RequiresContinuousCoverage)
        {
            var cursor = coverage.Minimum;
            while (cursor < coverage.Maximum)
            {
                var instance = CreateInstance(random, settings, assets, cursor, continuous: true);
                var overlap = instance.WorldWidth * random.Next(settings.MinimumSpacing, settings.MaximumSpacing);
                var center = cursor + instance.WorldWidth / 2f;
                instance = instance with { Position = new Vector2(center, instance.Position.Y) };
                instances.Add(instance);
                cursor += Math.Max(0.5f, instance.WorldWidth - overlap);
            }
        }
        else
        {
            var cursor = coverage.Minimum + random.Next(0f, settings.MinimumSpacing);
            while (cursor <= coverage.Maximum)
            {
                var instance = CreateInstance(random, settings, assets, cursor, continuous: false);
                instances.Add(instance);
                cursor += random.Next(settings.MinimumSpacing, settings.MaximumSpacing);
            }
        }
        return instances;
    }

    private static SceneryInstance CreateInstance(StableVisualRandom random, SceneryLayerSettings settings,
        IReadOnlyList<SceneryAssetDefinition> assets, float x, bool continuous)
    {
        var asset = SelectAsset(random, assets);
        var scale = random.Next(settings.MinimumScale, settings.MaximumScale);
        var height = settings.BaseHeight * scale;
        var width = height * asset.AspectRatio;
        var yJitter = continuous ? random.Next(-0.12f, 0.12f) : random.Next(-0.22f, 0.18f);
        return new SceneryInstance(asset.TextureId, new Vector2(x, settings.BaselineY + yJitter), scale,
            random.Next(0.90f, 1f), asset.AllowHorizontalFlip && random.NextBool(), width, height);
    }

    private static SceneryAssetDefinition SelectAsset(StableVisualRandom random,
        IReadOnlyList<SceneryAssetDefinition> assets)
    {
        var totalWeight = assets.Sum(asset => asset.Weight);
        var choice = random.Next(0f, totalWeight);
        foreach (var asset in assets)
        {
            choice -= asset.Weight;
            if (choice <= 0f) return asset;
        }
        return assets[^1];
    }

    private sealed class StableVisualRandom(ulong state)
    {
        private ulong _state = state;

        public float Next(float minimum, float maximum) => minimum + (maximum - minimum) * NextUnit();
        public bool NextBool() => (NextUInt64() & 1ul) != 0;

        private float NextUnit() => (NextUInt64() >> 40) / 16777216f;

        private ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15ul;
            var value = _state;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9ul;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBul;
            return value ^ (value >> 31);
        }
    }
}
