using System.Numerics;
using BirdHell.Systems;
using BirdHell.World.Geometry;

namespace BirdHell.World;

public readonly record struct NavigationCell(int X, int Y)
{
    public int ToIndex(int width) => Y * width + X;
}

public sealed class NavigationGrid
{
    public const int DefaultWidth = 128;
    public const int DefaultHeight = 72;
    public const float DefaultNodeSize = 1f;
    public const float OriginalObstacleScanRadius = 0.5f;
    private static readonly (int X, int Y)[] NeighborOffsets =
    [
        (0, 1), (1, 0), (0, -1), (-1, 0),
        (1, 1), (1, -1), (-1, -1), (-1, 1),
    ];
    private readonly bool[] _walkable;

    public NavigationGrid(CaveGeometry geometry)
        : this(new TerrainCollisionSystem(geometry).Segments)
    {
    }

    public NavigationGrid(
        IEnumerable<TerrainSegment> obstacleSegments,
        int width = DefaultWidth,
        int height = DefaultHeight,
        float nodeSize = DefaultNodeSize,
        Vector2? center = null,
        float scanRadius = OriginalObstacleScanRadius)
    {
        ArgumentNullException.ThrowIfNull(obstacleSegments);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (nodeSize <= 0f || !float.IsFinite(nodeSize)) throw new ArgumentOutOfRangeException(nameof(nodeSize));
        if (scanRadius < 0f || !float.IsFinite(scanRadius)) throw new ArgumentOutOfRangeException(nameof(scanRadius));
        Width = width;
        Height = height;
        NodeSize = nodeSize;
        Center = center ?? Vector2.Zero;
        Minimum = Center - new Vector2(width * nodeSize, height * nodeSize) / 2f;
        ScanRadius = scanRadius;
        _walkable = Enumerable.Repeat(true, width * height).ToArray();
        var segments = obstacleSegments.Where(segment => segment.A != segment.B).ToArray();
        var radiusSquared = scanRadius * scanRadius;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var point = CellToWorld(new NavigationCell(x, y));
            if (segments.Any(segment => TerrainCollisionSystem.DistanceSquaredToSegment(point, segment) <= radiusSquared))
                _walkable[new NavigationCell(x, y).ToIndex(width)] = false;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public float NodeSize { get; }
    public Vector2 Center { get; }
    public Vector2 Minimum { get; }
    public float ScanRadius { get; }
    public int CellCount => Width * Height;

    public bool IsInBounds(NavigationCell cell) => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;
    public bool IsWalkable(NavigationCell cell) => IsInBounds(cell) && _walkable[cell.ToIndex(Width)];
    public bool IsWalkable(int x, int y) => IsWalkable(new NavigationCell(x, y));
    public Vector2 CellToWorld(NavigationCell cell)
    {
        if (!IsInBounds(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
        return Minimum + new Vector2((cell.X + 0.5f) * NodeSize, (cell.Y + 0.5f) * NodeSize);
    }

    public NavigationCell WorldToCell(Vector2 world) => new(
        Math.Clamp((int)MathF.Floor((world.X - Minimum.X) / NodeSize), 0, Width - 1),
        Math.Clamp((int)MathF.Floor((world.Y - Minimum.Y) / NodeSize), 0, Height - 1));

    public IReadOnlyList<NavigationCell> GetNeighbors(NavigationCell cell)
    {
        var result = new List<NavigationCell>(8);
        foreach (var offset in NeighborOffsets)
        {
            var neighbor = new NavigationCell(cell.X + offset.X, cell.Y + offset.Y);
            if (!IsWalkable(neighbor)) continue;
            if (offset.X != 0 && offset.Y != 0 &&
                (!IsWalkable(cell.X + offset.X, cell.Y) || !IsWalkable(cell.X, cell.Y + offset.Y)))
                continue;
            result.Add(neighbor);
        }
        return result;
    }

    public bool TryFindNearestWalkable(Vector2 world, float maximumDistance, out NavigationCell result)
    {
        var maximumSquared = maximumDistance * maximumDistance;
        var bestDistance = float.PositiveInfinity;
        result = default;
        var found = false;
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var cell = new NavigationCell(x, y);
            if (!IsWalkable(cell)) continue;
            var distance = Vector2.DistanceSquared(world, CellToWorld(cell));
            if (distance > maximumSquared || distance >= bestDistance) continue;
            bestDistance = distance;
            result = cell;
            found = true;
        }
        return found;
    }
}
