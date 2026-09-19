using System.Collections.ObjectModel;

namespace BirdHell.World.Geometry;

public readonly record struct CaveVertex(float X, float Y);

public readonly record struct CaveTriangle(int A, int B, int C)
{
    public int this[int index] => index switch
    {
        0 => A,
        1 => B,
        2 => C,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public bool Contains(int vertexIndex) => A == vertexIndex || B == vertexIndex || C == vertexIndex;
}

public sealed class CaveOutline
{
    internal CaveOutline(IEnumerable<int> vertexIndices, IReadOnlyList<CaveVertex> vertices)
    {
        var indices = vertexIndices.ToArray();
        var points = new CaveVertex[indices.Length];
        for (var i = 0; i < indices.Length; i++) points[i] = vertices[indices[i]];
        VertexIndices = Array.AsReadOnly(indices);
        Points = Array.AsReadOnly(points);
    }

    public ReadOnlyCollection<int> VertexIndices { get; }
    public ReadOnlyCollection<CaveVertex> Points { get; }
    public bool IsClosed => VertexIndices.Count > 1 && VertexIndices[0] == VertexIndices[^1];
}

public sealed class CaveGeometry
{
    internal CaveGeometry(
        IEnumerable<CaveVertex> vertices,
        IEnumerable<CaveTriangle> triangles,
        IEnumerable<IEnumerable<int>> outlines,
        float squareSize,
        int borderedWidth,
        int borderedHeight)
    {
        var vertexArray = vertices.ToArray();
        var triangleArray = triangles.ToArray();
        Vertices = Array.AsReadOnly(vertexArray);
        Triangles = Array.AsReadOnly(triangleArray);
        Outlines = Array.AsReadOnly(outlines.Select(outline => new CaveOutline(outline, Vertices)).ToArray());
        SquareSize = squareSize;
        BorderedWidth = borderedWidth;
        BorderedHeight = borderedHeight;
        MinX = -borderedWidth * squareSize / 2f + squareSize / 2f;
        MaxX = -MinX;
        MinY = -borderedHeight * squareSize / 2f + squareSize / 2f;
        MaxY = -MinY;
    }

    public ReadOnlyCollection<CaveVertex> Vertices { get; }
    public ReadOnlyCollection<CaveTriangle> Triangles { get; }
    public ReadOnlyCollection<CaveOutline> Outlines { get; }
    public float SquareSize { get; }
    public int BorderedWidth { get; }
    public int BorderedHeight { get; }
    public float MinX { get; }
    public float MaxX { get; }
    public float MinY { get; }
    public float MaxY { get; }
}
