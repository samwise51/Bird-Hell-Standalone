using System.Numerics;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace BirdHell.Presentation;

public readonly record struct CloudSettings(
    uint VisualSeed,
    int TextureWidth,
    int TextureHeight,
    int Octaves,
    float Frequency,
    float Coverage,
    float Opacity,
    float VisualScale,
    Vector2 DriftSpeed,
    float EvolutionSpeed,
    ParallaxLayerSettings Layer)
{
    public static CloudSettings Default => new(
        VisualSeed: 0xA17E_51D5u,
        TextureWidth: 512,
        TextureHeight: 256,
        Octaves: 5,
        Frequency: 3f,
        Coverage: 0.61f,
        Opacity: 0.17f,
        VisualScale: 1.40f,
        DriftSpeed: new Vector2(0.018f, 0.002f),
        EvolutionSpeed: 0.025f,
        Layer: new ParallaxLayerSettings(0.04f, 0.015f, 1f, Vector2.Zero, Vector2.Zero,
            SpriteTextureFilter.Linear));

    public void Validate()
    {
        if (TextureWidth <= 0 || TextureHeight <= 0) throw new ArgumentOutOfRangeException(nameof(TextureWidth));
        if (Octaves is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(Octaves));
        if (!float.IsFinite(Frequency) || Frequency <= 0f) throw new ArgumentOutOfRangeException(nameof(Frequency));
        if (!float.IsFinite(Coverage) || Coverage is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(Coverage));
        if (!float.IsFinite(Opacity) || Opacity is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(Opacity));
        if (!float.IsFinite(VisualScale) || VisualScale <= 0f) throw new ArgumentOutOfRangeException(nameof(VisualScale));
        if (!float.IsFinite(DriftSpeed.X) || !float.IsFinite(DriftSpeed.Y))
            throw new ArgumentOutOfRangeException(nameof(DriftSpeed));
        if (!float.IsFinite(EvolutionSpeed) || EvolutionSpeed < 0f)
            throw new ArgumentOutOfRangeException(nameof(EvolutionSpeed));
    }
}

/// <summary>Presentation clock and preference state. Gameplay resets deliberately preserve both.</summary>
public sealed class CloudAtmosphereState
{
    public double ElapsedSeconds { get; private set; }
    public bool IsVisible { get; private set; } = true;

    public void Advance(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        ElapsedSeconds += elapsedSeconds;
    }

    public void ToggleVisibility() => IsVisible = !IsVisible;

    /// <summary>R/F5 replace gameplay only; the atmosphere remains the same world and time stream.</summary>
    public void OnGameplayReset()
    {
    }

    public CloudMotion GetMotion(CloudSettings settings)
    {
        settings.Validate();
        var elapsed = (float)ElapsedSeconds;
        var phase = elapsed * settings.EvolutionSpeed;
        var firstFrame = (int)MathF.Floor(phase) % CloudFieldGenerator.TemporalFrameCount;
        var blend = phase - MathF.Floor(phase);
        return new CloudMotion(settings.DriftSpeed * elapsed, firstFrame,
            (firstFrame + 1) % CloudFieldGenerator.TemporalFrameCount, blend);
    }
}

public readonly record struct CloudMotion(Vector2 Drift, int FirstFrame, int SecondFrame, float Blend);

/// <summary>Deterministic, tileable, anisotropic fBm alpha fields generated once at startup.</summary>
public static class CloudFieldGenerator
{
    public const int TemporalFrameCount = 4;

    public static XnaColor[] Generate(CloudSettings settings, int temporalFrame)
    {
        settings.Validate();
        if (temporalFrame is < 0 or >= TemporalFrameCount)
            throw new ArgumentOutOfRangeException(nameof(temporalFrame));

        var pixels = new XnaColor[settings.TextureWidth * settings.TextureHeight];
        var phaseSeed = settings.VisualSeed + (uint)temporalFrame * 0x9E37_79B9u;
        for (var y = 0; y < settings.TextureHeight; y++)
        for (var x = 0; x < settings.TextureWidth; x++)
        {
            var u = x / (float)settings.TextureWidth;
            var v = y / (float)settings.TextureHeight;
            var warp = Fractal(u, v, settings, phaseSeed ^ 0xB529_7A4Du, 2, 1.7f, 3.5f);
            var warpedV = v + (warp - 0.5f) * 0.095f;
            var broad = Fractal(u, warpedV, settings, phaseSeed, settings.Octaves, 1f, 4.2f);
            var filaments = 1f - MathF.Abs(Fractal(u + warp * 0.04f, warpedV, settings,
                phaseSeed ^ 0x68E3_1DA4u, Math.Min(settings.Octaves, 4), 2.2f, 7.5f) * 2f - 1f);
            var density = broad * 0.78f + filaments * 0.22f;
            var alpha = SmoothStep(settings.Coverage, MathF.Min(1f, settings.Coverage + 0.22f), density);
            alpha *= alpha * (0.62f + filaments * 0.38f);
            var alphaByte = (byte)Math.Clamp((int)MathF.Round(alpha * 155f), 0, 155);
            pixels[y * settings.TextureWidth + x] = XnaColor.FromNonPremultiplied(176, 193, 220, alphaByte);
        }
        return pixels;
    }

    public static ulong Fingerprint(ReadOnlySpan<XnaColor> pixels)
    {
        const ulong offset = 14695981039346656037ul;
        const ulong prime = 1099511628211ul;
        var hash = offset;
        foreach (var pixel in pixels)
        {
            hash = (hash ^ pixel.PackedValue) * prime;
        }
        return hash;
    }

    private static float Fractal(float u, float v, CloudSettings settings, uint seed, int octaves,
        float xFrequencyMultiplier, float yFrequencyMultiplier)
    {
        var sum = 0f;
        var weight = 0f;
        var amplitude = 1f;
        var baseCells = Math.Max(1, (int)MathF.Round(settings.Frequency));
        for (var octave = 0; octave < octaves; octave++)
        {
            var cellsX = Math.Max(1, (int)MathF.Round(baseCells * xFrequencyMultiplier) << octave);
            var cellsY = Math.Max(1, (int)MathF.Round(baseCells * yFrequencyMultiplier) << octave);
            sum += PeriodicValueNoise(u * cellsX, v * cellsY, cellsX, cellsY,
                seed + (uint)octave * 0x85EB_CA6Bu) * amplitude;
            weight += amplitude;
            amplitude *= 0.52f;
        }
        return sum / weight;
    }

    private static float PeriodicValueNoise(float x, float y, int periodX, int periodY, uint seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = Smooth(x - x0);
        var ty = Smooth(y - y0);
        var a = Hash01(Mod(x0, periodX), Mod(y0, periodY), seed);
        var b = Hash01(Mod(x0 + 1, periodX), Mod(y0, periodY), seed);
        var c = Hash01(Mod(x0, periodX), Mod(y0 + 1, periodY), seed);
        var d = Hash01(Mod(x0 + 1, periodX), Mod(y0 + 1, periodY), seed);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
    }

    private static float Hash01(int x, int y, uint seed)
    {
        var value = seed ^ (uint)x * 0x8DA6_B343u ^ (uint)y * 0xD816_3841u;
        value ^= value >> 16;
        value *= 0x7FEB_352Du;
        value ^= value >> 15;
        value *= 0x846C_A68Bu;
        value ^= value >> 16;
        return (value & 0x00FF_FFFFu) / 16777215f;
    }

    private static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;
    private static float Smooth(float value) => value * value * (3f - 2f * value);
    private static float SmoothStep(float low, float high, float value) =>
        high <= low ? (value >= high ? 1f : 0f) : Smooth(Math.Clamp((value - low) / (high - low), 0f, 1f));
    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;
}
