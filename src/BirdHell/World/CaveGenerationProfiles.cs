namespace BirdHell.World;

public enum CaveGenerationProfile
{
    Baseline,
    ConservativeIrregular,
    Dense,
    Complex,
    Extreme,
}

public sealed record CaveGenerationProfileSelection
{
    internal CaveGenerationProfileSelection(
        CaveGenerationProfile profile,
        string name,
        CaveGenerationOptions options)
    {
        Profile = profile;
        Name = name;
        Options = options;
    }

    public CaveGenerationProfile Profile { get; }
    public string Name { get; }
    public CaveGenerationOptions Options { get; }
}

/// <summary>Named generation configurations for deterministic comparison and development playtesting.</summary>
public static class CaveGenerationProfiles
{
    public const string BaselineName = "baseline";
    public const string ConservativeIrregularName = "conservative-irregular";
    public const string DenseName = "dense";
    public const string ComplexName = "complex";
    public const string ExtremeName = "extreme";

    public static CaveGenerationOptions Baseline => new();

    public static CaveGenerationOptions ConservativeIrregular => new()
    {
        Width = CaveGenerationOptions.DefaultWidth,
        Height = CaveGenerationOptions.DefaultHeight,
        RandomFillPercent = 38,
        SmoothingPasses = 4,
        WallRegionThreshold = 40,
        RoomRegionThreshold = 50,
        PassageRadius = 4,
    };

    public static CaveGenerationOptions Dense => new()
    {
        Width = CaveGenerationOptions.DefaultWidth,
        Height = CaveGenerationOptions.DefaultHeight,
        RandomFillPercent = 42,
        SmoothingPasses = 4,
        WallRegionThreshold = 40,
        RoomRegionThreshold = 50,
        PassageRadius = 4,
    };

    public static CaveGenerationOptions Complex => new()
    {
        Width = CaveGenerationOptions.DefaultWidth,
        Height = CaveGenerationOptions.DefaultHeight,
        RandomFillPercent = 45,
        SmoothingPasses = 3,
        WallRegionThreshold = 35,
        RoomRegionThreshold = 50,
        PassageRadius = 3,
    };

    public static CaveGenerationOptions Extreme => new()
    {
        Width = CaveGenerationOptions.DefaultWidth,
        Height = CaveGenerationOptions.DefaultHeight,
        RandomFillPercent = 48,
        SmoothingPasses = 3,
        WallRegionThreshold = 35,
        RoomRegionThreshold = 50,
        PassageRadius = 3,
    };

    public static CaveGenerationProfileSelection Default => Select(CaveGenerationProfile.Baseline);

    public static CaveGenerationProfileSelection Select(CaveGenerationProfile profile) => profile switch
    {
        CaveGenerationProfile.Baseline => new CaveGenerationProfileSelection(profile, BaselineName, Baseline),
        CaveGenerationProfile.ConservativeIrregular => new CaveGenerationProfileSelection(
            profile, ConservativeIrregularName, ConservativeIrregular),
        CaveGenerationProfile.Dense => new CaveGenerationProfileSelection(profile, DenseName, Dense),
        CaveGenerationProfile.Complex => new CaveGenerationProfileSelection(profile, ComplexName, Complex),
        CaveGenerationProfile.Extreme => new CaveGenerationProfileSelection(profile, ExtremeName, Extreme),
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown cave-generation profile."),
    };

    public static CaveGenerationProfileSelection Resolve(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name switch
        {
            BaselineName => Select(CaveGenerationProfile.Baseline),
            ConservativeIrregularName => Select(CaveGenerationProfile.ConservativeIrregular),
            DenseName => Select(CaveGenerationProfile.Dense),
            ComplexName => Select(CaveGenerationProfile.Complex),
            ExtremeName => Select(CaveGenerationProfile.Extreme),
            _ => throw new ArgumentException(
                $"Unknown cave profile '{name}'. Expected {ExpectedNames}.",
                nameof(name)),
        };
    }

    public static string ExpectedNames =>
        $"'{BaselineName}', '{ConservativeIrregularName}', '{DenseName}', '{ComplexName}', or '{ExtremeName}'";
}

public static class CaveProfileCommandLine
{
    public const string OptionName = "--cave-profile";

    public static CaveGenerationProfileSelection Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        CaveGenerationProfileSelection? selection = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!string.Equals(argument, OptionName, StringComparison.Ordinal))
                throw new ArgumentException($"Unknown argument '{argument}'. Expected '{OptionName} <profile>'.");
            if (selection is not null)
                throw new ArgumentException($"Option '{OptionName}' may be specified only once.");
            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Option '{OptionName}' requires one of: {CaveGenerationProfiles.ExpectedNames}.");
            selection = CaveGenerationProfiles.Resolve(arguments[++index]);
        }
        return selection ?? CaveGenerationProfiles.Default;
    }
}
