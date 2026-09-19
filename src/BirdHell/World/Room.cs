namespace BirdHell.World;

internal sealed class Room : IComparable<Room>
{
    public Room(List<GridCoordinate> tiles, int[,] map)
    {
        Tiles = tiles;
        Size = tiles.Count;
        ConnectedRooms = [];
        EdgeTiles = [];

        foreach (var tile in tiles)
        for (var x = tile.X - 1; x <= tile.X + 1; x++)
        for (var y = tile.Y - 1; y <= tile.Y + 1; y++)
            if ((x == tile.X || y == tile.Y) && map[x, y] == 1)
                EdgeTiles.Add(tile);
    }

    public List<GridCoordinate> Tiles { get; }
    public List<GridCoordinate> EdgeTiles { get; }
    public List<Room> ConnectedRooms { get; }
    public int Size { get; }
    public bool IsAccessibleFromMainRoom { get; private set; }
    public bool IsMainRoom { get; private set; }

    public void MarkAsMainRoom()
    {
        IsMainRoom = true;
        IsAccessibleFromMainRoom = true;
    }

    public void SetAccessibleFromMainRoom()
    {
        if (IsAccessibleFromMainRoom) return;
        IsAccessibleFromMainRoom = true;
        foreach (var connectedRoom in ConnectedRooms)
            connectedRoom.SetAccessibleFromMainRoom();
    }

    public static void Connect(Room roomA, Room roomB)
    {
        if (roomA.IsAccessibleFromMainRoom) roomB.SetAccessibleFromMainRoom();
        else if (roomB.IsAccessibleFromMainRoom) roomA.SetAccessibleFromMainRoom();
        roomA.ConnectedRooms.Add(roomB);
        roomB.ConnectedRooms.Add(roomA);
    }

    public bool IsConnected(Room otherRoom) => ConnectedRooms.Contains(otherRoom);
    public int CompareTo(Room? other) => other is null ? -1 : other.Size.CompareTo(Size);
}
