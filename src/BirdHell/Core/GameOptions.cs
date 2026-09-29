using BirdHell.World;

namespace BirdHell.Core;

public sealed class GameOptions
{
    public const int MinimumBirdCount = 1;
    public const int MaximumBirdCount = 50;
    public const int DefaultBirdCount = 15;

    public GameOptions(CaveGenerationProfileSelection? caveProfile = null)
    {
        CaveProfile = caveProfile ?? CaveGenerationProfiles.Default;
    }

    public int BirdCount { get; private set; } = DefaultBirdCount;
    public CaveGenerationProfileSelection CaveProfile { get; private set; }
    public bool Invincible { get; private set; }
    public bool Fullscreen { get; private set; }

    public void AdjustBirdCount(int delta) =>
        BirdCount = Math.Clamp(BirdCount + delta, MinimumBirdCount, MaximumBirdCount);

    public void SetBirdCount(int count)
    {
        if (count < MinimumBirdCount || count > MaximumBirdCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        BirdCount = count;
    }

    public void CycleCaveProfile(int direction)
    {
        var profiles = new[]
        {
            CaveGenerationProfile.Baseline,
            CaveGenerationProfile.Complex,
            CaveGenerationProfile.Extreme,
        };
        var index = Array.IndexOf(profiles, CaveProfile.Profile);
        if (index < 0) index = 0;
        index = (index + Math.Sign(direction) + profiles.Length) % profiles.Length;
        CaveProfile = CaveGenerationProfiles.Select(profiles[index]);
    }

    public void ToggleInvincibility() => Invincible = !Invincible;
    public void ToggleFullscreen() => Fullscreen = !Fullscreen;

    public string CaveProfileDisplayName => CaveProfile.Profile switch
    {
        CaveGenerationProfile.Baseline => "Standard",
        CaveGenerationProfile.Complex => "Complex",
        CaveGenerationProfile.Extreme => "Extreme",
        _ => CaveProfile.Name,
    };
}
