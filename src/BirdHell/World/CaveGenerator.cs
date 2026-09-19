using System.Globalization;

namespace BirdHell.World;

public sealed class CaveGenerator
{
    private readonly CaveGenerationOptions _options;
    private int[,] _map = new int[0, 0];

    public CaveGenerator(CaveGenerationOptions? options = null)
    {
        _options = options ?? new CaveGenerationOptions();
        ValidateOptions(_options);
    }

    public CaveMap Generate(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _map = new int[_options.Width, _options.Height];
        RandomFillMap(seed);
        for (var i = 0; i < _options.SmoothingPasses; i++) SmoothMap();
        ProcessMap();
        return new CaveMap(_map);
    }

    public static string CreateRandomSeed() => string.Create(
        CultureInfo.InvariantCulture,
        $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}");

    private static void ValidateOptions(CaveGenerationOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Height);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RandomFillPercent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.RandomFillPercent, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(options.SmoothingPasses);
        ArgumentOutOfRangeException.ThrowIfNegative(options.WallRegionThreshold);
        ArgumentOutOfRangeException.ThrowIfNegative(options.RoomRegionThreshold);
        ArgumentOutOfRangeException.ThrowIfNegative(options.PassageRadius);
    }

    private void RandomFillMap(string seed)
    {
        var pseudoRandom = new Random(GetStableSeedHash(seed));
        for (var x = 0; x < _options.Width; x++)
        for (var y = 0; y < _options.Height; y++)
            _map[x, y] = x == 0 || x == _options.Width - 1 || y == 0 || y == _options.Height - 1
                ? 1
                : pseudoRandom.Next(0, 100) < _options.RandomFillPercent ? 1 : 0;
    }

    private static int GetStableSeedHash(string seed)
    {
        const uint prime = 16777619;
        var hash = 2166136261u;
        foreach (var character in seed)
        {
            hash ^= character;
            hash *= prime;
        }
        return unchecked((int)hash);
    }

    private void SmoothMap()
    {
        for (var x = 0; x < _options.Width; x++)
        for (var y = 0; y < _options.Height; y++)
        {
            var neighbourWallTiles = GetSurroundingWallCount(x, y);
            if (neighbourWallTiles > 4) _map[x, y] = 1;
            else if (neighbourWallTiles < 4) _map[x, y] = 0;
        }
    }

    private int GetSurroundingWallCount(int gridX, int gridY)
    {
        var wallCount = 0;
        for (var neighbourX = gridX - 1; neighbourX <= gridX + 1; neighbourX++)
        for (var neighbourY = gridY - 1; neighbourY <= gridY + 1; neighbourY++)
        {
            if (IsInMapRange(neighbourX, neighbourY))
            {
                if (neighbourX != gridX || neighbourY != gridY)
                    wallCount += _map[neighbourX, neighbourY];
            }
            else wallCount++;
        }
        return wallCount;
    }

    private void ProcessMap()
    {
        foreach (var wallRegion in GetRegions(1))
            if (wallRegion.Count < _options.WallRegionThreshold)
                foreach (var tile in wallRegion) _map[tile.X, tile.Y] = 0;

        var survivingRooms = new List<Room>();
        foreach (var roomRegion in GetRegions(0))
        {
            if (roomRegion.Count < _options.RoomRegionThreshold)
                foreach (var tile in roomRegion) _map[tile.X, tile.Y] = 1;
            else survivingRooms.Add(new Room(roomRegion, _map));
        }

        survivingRooms.Sort();
        if (survivingRooms.Count == 0)
            throw new InvalidOperationException("Cave generation produced no room at or above the configured room threshold.");
        survivingRooms[0].MarkAsMainRoom();
        ConnectClosestRooms(survivingRooms);
    }

    private void ConnectClosestRooms(List<Room> allRooms, bool forceAccessibilityFromMainRoom = false)
    {
        List<Room> roomListA;
        List<Room> roomListB;
        if (forceAccessibilityFromMainRoom)
        {
            roomListA = [];
            roomListB = [];
            foreach (var room in allRooms)
                (room.IsAccessibleFromMainRoom ? roomListB : roomListA).Add(room);
        }
        else
        {
            roomListA = allRooms;
            roomListB = allRooms;
        }

        var bestDistance = 0;
        var bestTileA = default(GridCoordinate);
        var bestTileB = default(GridCoordinate);
        Room? bestRoomA = null;
        Room? bestRoomB = null;
        var possibleConnectionFound = false;

        foreach (var roomA in roomListA)
        {
            if (!forceAccessibilityFromMainRoom)
            {
                possibleConnectionFound = false;
                if (roomA.ConnectedRooms.Count > 0) continue;
            }

            foreach (var roomB in roomListB)
            {
                if (roomA == roomB || roomA.IsConnected(roomB)) continue;
                for (var tileIndexA = 0; tileIndexA < roomA.EdgeTiles.Count; tileIndexA++)
                for (var tileIndexB = 0; tileIndexB < roomB.EdgeTiles.Count; tileIndexB++)
                {
                    var tileA = roomA.EdgeTiles[tileIndexA];
                    var tileB = roomB.EdgeTiles[tileIndexB];
                    var deltaX = tileA.X - tileB.X;
                    var deltaY = tileA.Y - tileB.Y;
                    var distanceBetweenRooms = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceBetweenRooms < bestDistance || !possibleConnectionFound)
                    {
                        bestDistance = distanceBetweenRooms;
                        possibleConnectionFound = true;
                        bestTileA = tileA;
                        bestTileB = tileB;
                        bestRoomA = roomA;
                        bestRoomB = roomB;
                    }
                }
            }
            if (possibleConnectionFound && !forceAccessibilityFromMainRoom)
                CreatePassage(bestRoomA!, bestRoomB!, bestTileA, bestTileB);
        }

        if (possibleConnectionFound && forceAccessibilityFromMainRoom)
        {
            CreatePassage(bestRoomA!, bestRoomB!, bestTileA, bestTileB);
            ConnectClosestRooms(allRooms, true);
        }
        if (!forceAccessibilityFromMainRoom) ConnectClosestRooms(allRooms, true);
    }

    private void CreatePassage(Room roomA, Room roomB, GridCoordinate tileA, GridCoordinate tileB)
    {
        Room.Connect(roomA, roomB);
        foreach (var coordinate in GetLine(tileA, tileB)) DrawCircle(coordinate, _options.PassageRadius);
    }

    private void DrawCircle(GridCoordinate coordinate, int radius)
    {
        for (var x = -radius; x <= radius; x++)
        for (var y = -radius; y <= radius; y++)
            if ((x * x) + (y * y) <= radius * radius)
            {
                var drawX = coordinate.X + x;
                var drawY = coordinate.Y + y;
                if (IsInMapRange(drawX, drawY)) _map[drawX, drawY] = 0;
            }
    }

    private static List<GridCoordinate> GetLine(GridCoordinate from, GridCoordinate to)
    {
        var line = new List<GridCoordinate>();
        var x = from.X;
        var y = from.Y;
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        var inverted = false;
        var step = Math.Sign(deltaX);
        var gradientStep = Math.Sign(deltaY);
        var longest = Math.Abs(deltaX);
        var shortest = Math.Abs(deltaY);
        if (longest < shortest)
        {
            inverted = true;
            longest = Math.Abs(deltaY);
            shortest = Math.Abs(deltaX);
            step = Math.Sign(deltaY);
            gradientStep = Math.Sign(deltaX);
        }

        var gradientAccumulation = longest / 2;
        for (var i = 0; i < longest; i++)
        {
            line.Add(new GridCoordinate(x, y));
            if (inverted) y += step;
            else x += step;
            gradientAccumulation += shortest;
            if (gradientAccumulation >= longest)
            {
                if (inverted) x += gradientStep;
                else y += gradientStep;
                gradientAccumulation -= longest;
            }
        }
        return line;
    }

    private List<List<GridCoordinate>> GetRegions(int tileType)
    {
        var regions = new List<List<GridCoordinate>>();
        var mapFlags = new int[_options.Width, _options.Height];
        for (var x = 0; x < _options.Width; x++)
        for (var y = 0; y < _options.Height; y++)
            if (mapFlags[x, y] == 0 && _map[x, y] == tileType)
            {
                var newRegion = GetRegionTiles(x, y);
                regions.Add(newRegion);
                foreach (var tile in newRegion) mapFlags[tile.X, tile.Y] = 1;
            }
        return regions;
    }

    private List<GridCoordinate> GetRegionTiles(int startX, int startY)
    {
        var tiles = new List<GridCoordinate>();
        var mapFlags = new int[_options.Width, _options.Height];
        var tileType = _map[startX, startY];
        var queue = new Queue<GridCoordinate>();
        queue.Enqueue(new GridCoordinate(startX, startY));
        mapFlags[startX, startY] = 1;
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            tiles.Add(tile);
            for (var x = tile.X - 1; x <= tile.X + 1; x++)
            for (var y = tile.Y - 1; y <= tile.Y + 1; y++)
                if (IsInMapRange(x, y) && (y == tile.Y || x == tile.X) && mapFlags[x, y] == 0 && _map[x, y] == tileType)
                {
                    mapFlags[x, y] = 1;
                    queue.Enqueue(new GridCoordinate(x, y));
                }
        }
        return tiles;
    }

    private bool IsInMapRange(int x, int y) => x >= 0 && x < _options.Width && y >= 0 && y < _options.Height;
}
