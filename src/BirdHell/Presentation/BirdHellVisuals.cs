using System.Numerics;

namespace BirdHell.Presentation;

/// <summary>Audited Unity sprite regions and effective animation rates encoded for the standalone port.</summary>
public static class BirdHellVisuals
{
    public const string PlayerTexture = "Sprites/Player/adventurer-v1.5-Sheet.png";
    public const string ProjectileTexture = "Sprites/Projectile/sword-attack-animation.png";
    public const string BackgroundTexture = "Sprites/Background/back.png";
    public const float PixelsPerUnit = 100f;
    public const SpriteTextureFilter BackgroundFilter = SpriteTextureFilter.Point;
    public const HorizontalFacing EnemyNativeFacing = HorizontalFacing.Left;
    public static readonly Vector2 BackgroundLocalPosition = new(-2.66f, -5f);
    public static readonly Vector2 BackgroundWorldSize = new(38.4f, 24f);

    public static SpriteAnimationClip PlayerIdle { get; } = PlayerClip("Idle", [0, 1, 2, 3], 12f * 0.35f, true);
    public static SpriteAnimationClip PlayerRun { get; } = PlayerClip("Run", [8, 9, 10, 11], 12f, true);
    public static SpriteAnimationClip PlayerJump { get; } = PlayerClip("Jump", [12, 13, 14, 15], 60f * 0.2f, true);
    public static SpriteAnimationClip PlayerRoll { get; } = PlayerClip("Roll", [18, 19, 20, 21, 17], 10f, false);
    public static SpriteAnimationClip PlayerCrouch { get; } = PlayerClip("Crouch", [4, 5, 6, 7], 12f * 0.5f, true);
    public static SpriteAnimationClip PlayerAttack { get; } = PlayerClip("Attack", [47, 48, 49, 50, 47], 60f * 1.5f, false);

    public static SpriteAnimationClip Enemy { get; } = new(
        "Eagle",
        Enumerable.Range(1, 4).Select(index => new SpriteFrame(
            new SpriteRegion($"Sprites/Enemy/eagle-attack-{index}.png", new PixelRectangle(0, 0, 40, 41),
                new Vector2(0.5f), PixelsPerUnit, 3f, SpriteTextureFilter.Point),
            1f / 12f)).ToArray(),
        true);

    public static SpriteAnimationClip Projectile { get; } = new(
        "Projectile",
        new[]
        {
            ProjectileFrame(0, 3, 68, 56),
            ProjectileFrame(88, 2, 62, 54),
            ProjectileFrame(171, 3, 52, 36),
            ProjectileFrame(246, 5, 43, 16),
        },
        true);

    private static SpriteAnimationClip PlayerClip(string name, int[] indices, float effectiveFramesPerSecond, bool loop) =>
        new(name, indices.Select(index => new SpriteFrame(PlayerRegion(index), 1f / effectiveFramesPerSecond)).ToArray(), loop);

    private static SpriteRegion PlayerRegion(int index) => new(
        PlayerTexture,
        new PixelRectangle(index % 7 * 50, 592 - (index / 7 + 1) * 37, 50, 37),
        new Vector2(0.5f),
        PixelsPerUnit,
        4f,
        SpriteTextureFilter.Linear,
        PixelCoordinateOrigin.BottomLeft);

    private static SpriteFrame ProjectileFrame(int x, int y, int width, int height) => new(
        new SpriteRegion(ProjectileTexture, new PixelRectangle(x, y, width, height), Vector2.Zero, PixelsPerUnit, 1f,
            SpriteTextureFilter.Linear,
            PixelCoordinateOrigin.BottomLeft),
        1f / 18f);
}
