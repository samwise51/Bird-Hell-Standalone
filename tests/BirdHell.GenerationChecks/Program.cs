using BirdHell.World;
using BirdHell.World.Geometry;

var generator = new CaveGenerator();
var first = generator.Generate("milestone-1-repeatable-seed");
var second = generator.Generate("milestone-1-repeatable-seed");

Assert(first.Width == CaveGenerationOptions.DefaultWidth, "Generated width does not match configuration.");
Assert(first.Height == CaveGenerationOptions.DefaultHeight, "Generated height does not match configuration.");
for (var x = 0; x < first.Width; x++)
    for (var y = 0; y < first.Height; y++)
    {
        Assert(first[x, y] is CaveTile.Open or CaveTile.Wall, $"Unexpected tile at ({x}, {y}).");
        Assert(first[x, y] == second[x, y], $"Same-seed maps differ at ({x}, {y}).");
    }

Console.WriteLine("PASS: default generation completed without throwing");
Console.WriteLine("PASS: generated dimensions match configured dimensions");
Console.WriteLine("PASS: identical explicit seeds produce identical maps");
Console.WriteLine("PASS: all cells contain valid logical tile values");

var geometryBuilder = new CaveGeometryBuilder();
var firstGeometry = geometryBuilder.Build(first);
var secondGeometry = geometryBuilder.Build(first);

Assert(firstGeometry.Vertices.Count > 0, "Default cave geometry contains no vertices.");
Assert(firstGeometry.Triangles.Count > 0, "Default cave geometry contains no triangles.");
Assert(firstGeometry.BorderedWidth == first.Width + 2, "Geometry did not add the original one-cell horizontal border.");
Assert(firstGeometry.BorderedHeight == first.Height + 2, "Geometry did not add the original one-cell vertical border.");
ValidateGeometry(firstGeometry);
Assert(firstGeometry.Vertices.SequenceEqual(secondGeometry.Vertices), "Repeated geometry builds produced different vertices.");
Assert(firstGeometry.Triangles.SequenceEqual(secondGeometry.Triangles), "Repeated geometry builds produced different triangles.");
Assert(
    firstGeometry.Outlines.Select(outline => outline.VertexIndices.ToArray())
        .SequenceEqual(secondGeometry.Outlines.Select(outline => outline.VertexIndices.ToArray()), IntArrayComparer.Instance),
    "Repeated geometry builds produced different outlines.");

var expectedCentralTriangleCounts = new[] { 0, 1, 1, 2, 1, 4, 2, 3, 1, 2, 4, 3, 2, 3, 3, 2 };
for (var configuration = 0; configuration <= 15; configuration++)
{
    var tinyGeometry = geometryBuilder.Build(CreateConfigurationMap(configuration));
    ValidateGeometry(tinyGeometry);
    var centralTriangles = tinyGeometry.Triangles.Where(triangle =>
        IsInCentralSquare(tinyGeometry.Vertices[triangle.A]) &&
        IsInCentralSquare(tinyGeometry.Vertices[triangle.B]) &&
        IsInCentralSquare(tinyGeometry.Vertices[triangle.C])).ToArray();
    Assert(
        centralTriangles.Length == expectedCentralTriangleCounts[configuration],
        $"Configuration {configuration} generated {centralTriangles.Length} central triangles; expected {expectedCentralTriangleCounts[configuration]}.");

    if (configuration is 5 or 10)
    {
        var centralVertexCount = centralTriangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .Count();
        Assert(centralVertexCount == 6, $"Ambiguous configuration {configuration} was not one connected six-vertex polygon.");
    }
}

Console.WriteLine("PASS: default cave geometry generation completed");
Console.WriteLine("PASS: geometry vertices, triangles, indices, outlines, and coordinates are valid");
Console.WriteLine("PASS: repeated geometry generation from one CaveMap is deterministic");
Console.WriteLine("PASS: hand-authored maps exercise configurations 0-15 with original triangle counts");
Console.WriteLine("PASS: ambiguous configurations 5 and 10 remain connected six-vertex polygons");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static CaveMap CreateConfigurationMap(int configuration)
{
    var tiles = new CaveTile[2, 2];
    tiles[0, 1] = (configuration & 8) != 0 ? CaveTile.Wall : CaveTile.Open;
    tiles[1, 1] = (configuration & 4) != 0 ? CaveTile.Wall : CaveTile.Open;
    tiles[1, 0] = (configuration & 2) != 0 ? CaveTile.Wall : CaveTile.Open;
    tiles[0, 0] = (configuration & 1) != 0 ? CaveTile.Wall : CaveTile.Open;
    return new CaveMap(tiles);
}

static bool IsInCentralSquare(CaveVertex vertex) =>
    vertex.X >= -0.5f && vertex.X <= 0.5f && vertex.Y >= -0.5f && vertex.Y <= 0.5f;

static void ValidateGeometry(CaveGeometry geometry)
{
    for (var i = 0; i < geometry.Vertices.Count; i++)
    {
        var vertex = geometry.Vertices[i];
        Assert(float.IsFinite(vertex.X) && float.IsFinite(vertex.Y), $"Vertex {i} has a non-finite coordinate.");
    }

    for (var i = 0; i < geometry.Triangles.Count; i++)
    {
        var triangle = geometry.Triangles[i];
        Assert(triangle.A >= 0 && triangle.A < geometry.Vertices.Count, $"Triangle {i} index A is invalid.");
        Assert(triangle.B >= 0 && triangle.B < geometry.Vertices.Count, $"Triangle {i} index B is invalid.");
        Assert(triangle.C >= 0 && triangle.C < geometry.Vertices.Count, $"Triangle {i} index C is invalid.");
    }

    for (var outlineIndex = 0; outlineIndex < geometry.Outlines.Count; outlineIndex++)
    {
        var outline = geometry.Outlines[outlineIndex];
        Assert(outline.VertexIndices.Count == outline.Points.Count, $"Outline {outlineIndex} index/point counts differ.");
        Assert(outline.IsClosed, $"Outline {outlineIndex} is not closed.");
        for (var i = 0; i < outline.VertexIndices.Count; i++)
        {
            var vertexIndex = outline.VertexIndices[i];
            Assert(vertexIndex >= 0 && vertexIndex < geometry.Vertices.Count, $"Outline {outlineIndex} has an invalid vertex index.");
            Assert(outline.Points[i] == geometry.Vertices[vertexIndex], $"Outline {outlineIndex} point does not match its vertex index.");
            Assert(float.IsFinite(outline.Points[i].X) && float.IsFinite(outline.Points[i].Y), $"Outline {outlineIndex} has a non-finite point.");
        }
    }
}

sealed class IntArrayComparer : IEqualityComparer<int[]>
{
    public static IntArrayComparer Instance { get; } = new();

    public bool Equals(int[]? first, int[]? second) =>
        ReferenceEquals(first, second) || first is not null && second is not null && first.SequenceEqual(second);

    public int GetHashCode(int[] values)
    {
        var hash = new HashCode();
        foreach (var value in values) hash.Add(value);
        return hash.ToHashCode();
    }
}
