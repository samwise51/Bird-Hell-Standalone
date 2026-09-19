namespace BirdHell.World;

public sealed class CaveMap
{
    private readonly CaveTile[,] _tiles;

    internal CaveMap(int[,] tiles)
    {
        Width = tiles.GetLength(0);
        Height = tiles.GetLength(1);
        _tiles = new CaveTile[Width, Height];

        for (var x = 0; x < Width; x++)
        for (var y = 0; y < Height; y++)
            _tiles[x, y] = tiles[x, y] == 1 ? CaveTile.Wall : CaveTile.Open;
    }

    public int Width { get; }
    public int Height { get; }
    public CaveTile this[int x, int y] => _tiles[x, y];
    public bool IsInBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
}
