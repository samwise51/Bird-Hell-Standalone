namespace BirdHell.World;

public sealed class CaveMap
{
    private readonly CaveTile[,] _tiles;

    internal CaveMap(int[,] tiles)
        : this(ConvertTiles(tiles))
    {
    }

    public CaveMap(CaveTile[,] tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        Width = tiles.GetLength(0);
        Height = tiles.GetLength(1);
        ArgumentOutOfRangeException.ThrowIfZero(Width);
        ArgumentOutOfRangeException.ThrowIfZero(Height);
        _tiles = new CaveTile[Width, Height];

        for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
            {
                if (tiles[x, y] is not (CaveTile.Open or CaveTile.Wall))
                    throw new ArgumentException($"Unsupported cave tile value at ({x}, {y}).", nameof(tiles));
                _tiles[x, y] = tiles[x, y];
            }
    }

    public int Width { get; }
    public int Height { get; }
    public CaveTile this[int x, int y] => _tiles[x, y];
    public bool IsInBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    private static CaveTile[,] ConvertTiles(int[,] tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        var converted = new CaveTile[tiles.GetLength(0), tiles.GetLength(1)];
        for (var x = 0; x < converted.GetLength(0); x++)
            for (var y = 0; y < converted.GetLength(1); y++)
                converted[x, y] = tiles[x, y] == 1 ? CaveTile.Wall : CaveTile.Open;
        return converted;
    }
}
