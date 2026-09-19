namespace BirdHell.World;

public sealed record CaveGenerationOptions
{
    public const int DefaultWidth = 128;
    public const int DefaultHeight = 30;
    public const int DefaultRandomFillPercent = 35;
    public const int DefaultSmoothingPasses = 5;
    public const int DefaultWallRegionThreshold = 50;
    public const int DefaultRoomRegionThreshold = 50;
    public const int DefaultPassageRadius = 5;

    public int Width { get; init; } = DefaultWidth;
    public int Height { get; init; } = DefaultHeight;
    public int RandomFillPercent { get; init; } = DefaultRandomFillPercent;
    public int SmoothingPasses { get; init; } = DefaultSmoothingPasses;
    public int WallRegionThreshold { get; init; } = DefaultWallRegionThreshold;
    public int RoomRegionThreshold { get; init; } = DefaultRoomRegionThreshold;
    public int PassageRadius { get; init; } = DefaultPassageRadius;
}
