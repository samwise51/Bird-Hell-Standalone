using System.Numerics;

namespace BirdHell.Presentation;

/// <summary>Audited Unity sprite regions and effective animation rates encoded for the standalone port.</summary>
public static class BirdHellVisuals
{
    public const string PlayerTexture = "Sprites/Player/player_explorer_runtime_v1.png";
    public const string ProjectileTexture = "Sprites/Projectile/sword-attack-animation.png";
    public const string BackgroundTexture = "Sprites/Background/back.png";
    public const float PixelsPerUnit = 100f;
    public const int PlayerAtlasWidth = 1728;
    public const int PlayerAtlasHeight = 1056;
    public const int PlayerCellWidth = 192;
    public const int PlayerCellHeight = 176;
    public const float PlayerWorldScale = 1.2f;
    public const float PlayerReferenceVisibleBodyHeightPixels = 123f;
    public const SpriteTextureFilter BackgroundFilter = SpriteTextureFilter.Point;
    public const HorizontalFacing PlayerNativeFacing = HorizontalFacing.Right;
    public const HorizontalFacing EnemyNativeFacing = HorizontalFacing.Left;
    public static readonly Vector2 PlayerBodyPivotPixels = new(96f, 88f);
    public static readonly Vector2 PlayerGroundBaselinePixels = new(96f, 160f);
    public static readonly Vector2 BackgroundLocalPosition = new(-2.66f, -5f);
    public static readonly Vector2 BackgroundWorldSize = new(38.4f, 24f);

    public static SpriteAnimationClip PlayerIdle { get; } = PlayerClip("Idle", 0, 0, 9, 7f, true);
    public static SpriteAnimationClip PlayerRun { get; } = PlayerClip("Run", 1, 0, 8, 12f, true);
    public static SpriteAnimationClip PlayerJump { get; } = PlayerClip("Jump", 2, 0, 6, 10f, false);
    public static SpriteAnimationClip PlayerAirborne { get; } = PlayerClip("Airborne", 3, 0, 1, 0f, true);
    public static SpriteAnimationClip PlayerCrouch { get; } = PlayerClip("Crouch", 3, 1, 1, 0f, true);
    public static SpriteAnimationClip PlayerRoll { get; } = PlayerClip("Roll", 4, 0, 6, 12f, false);
    public static SpriteAnimationClip PlayerAttack { get; } = PlayerClip("Attack", 5, 0, 7, 14f, false);

    public static IReadOnlyList<SpriteAnimationClip> PlayerClips { get; } =
    [
        PlayerIdle, PlayerRun, PlayerJump, PlayerAirborne, PlayerCrouch, PlayerRoll, PlayerAttack,
    ];

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

    private static SpriteAnimationClip PlayerClip(
        string name,
        int row,
        int firstColumn,
        int frameCount,
        float framesPerSecond,
        bool loop)
    {
        var frameDuration = framesPerSecond > 0f ? 1f / framesPerSecond : 1f;
        return new SpriteAnimationClip(
            name,
            Enumerable.Range(firstColumn, frameCount)
                .Select(column => new SpriteFrame(PlayerRegion(column, row), frameDuration))
                .ToArray(),
            loop);
    }

    private static SpriteRegion PlayerRegion(int column, int row) => new(
        PlayerTexture,
        new PixelRectangle(column * PlayerCellWidth, row * PlayerCellHeight, PlayerCellWidth, PlayerCellHeight),
        new Vector2(
            PlayerBodyPivotPixels.X / PlayerCellWidth,
            PlayerBodyPivotPixels.Y / PlayerCellHeight),
        PixelsPerUnit,
        PlayerWorldScale,
        SpriteTextureFilter.Linear);

    private static SpriteFrame ProjectileFrame(int x, int y, int width, int height) => new(
        new SpriteRegion(ProjectileTexture, new PixelRectangle(x, y, width, height), Vector2.Zero, PixelsPerUnit, 1f,
            SpriteTextureFilter.Linear,
            PixelCoordinateOrigin.BottomLeft),
        1f / 18f);
}
