namespace BirdHell.World.Geometry;

public sealed class CaveGeometryBuilder
{
    public const float DefaultSquareSize = 1f;
    public const int BorderSize = 1;

    private readonly List<CaveVertex> _vertices = [];
    private readonly List<CaveTriangle> _triangles = [];
    private readonly Dictionary<int, List<CaveTriangle>> _trianglesByVertex = [];
    private readonly List<List<int>> _outlines = [];
    private readonly HashSet<int> _checkedVertices = [];

    public CaveGeometry Build(CaveMap map, float squareSize = DefaultSquareSize)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!float.IsFinite(squareSize) || squareSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(squareSize), "Square size must be finite and greater than zero.");

        _vertices.Clear();
        _triangles.Clear();
        _trianglesByVertex.Clear();
        _outlines.Clear();
        _checkedVertices.Clear();

        var samples = CreateBorderedSamples(map);
        var squareGrid = new SquareGrid(samples, squareSize);
        for (var x = 0; x < squareGrid.Squares.GetLength(0); x++)
            for (var y = 0; y < squareGrid.Squares.GetLength(1); y++)
                TriangulateSquare(squareGrid.Squares[x, y]);

        CalculateMeshOutlines();
        return new CaveGeometry(
            _vertices,
            _triangles,
            _outlines,
            squareSize,
            samples.GetLength(0),
            samples.GetLength(1));
    }

    private static bool[,] CreateBorderedSamples(CaveMap map)
    {
        var samples = new bool[map.Width + BorderSize * 2, map.Height + BorderSize * 2];
        for (var x = 0; x < samples.GetLength(0); x++)
            for (var y = 0; y < samples.GetLength(1); y++)
                samples[x, y] = x < BorderSize || x >= map.Width + BorderSize ||
                                y < BorderSize || y >= map.Height + BorderSize ||
                                map[x - BorderSize, y - BorderSize] == CaveTile.Wall;
        return samples;
    }

    private void TriangulateSquare(Square square)
    {
        switch (square.Configuration)
        {
            case 0: break;
            case 1: MeshFromPoints(square.CentreLeft, square.CentreBottom, square.BottomLeft); break;
            case 2: MeshFromPoints(square.BottomRight, square.CentreBottom, square.CentreRight); break;
            case 3: MeshFromPoints(square.CentreRight, square.BottomRight, square.BottomLeft, square.CentreLeft); break;
            case 4: MeshFromPoints(square.TopRight, square.CentreRight, square.CentreTop); break;
            case 5: MeshFromPoints(square.CentreTop, square.TopRight, square.CentreRight, square.CentreBottom, square.BottomLeft, square.CentreLeft); break;
            case 6: MeshFromPoints(square.CentreTop, square.TopRight, square.BottomRight, square.CentreBottom); break;
            case 7: MeshFromPoints(square.CentreTop, square.TopRight, square.BottomRight, square.BottomLeft, square.CentreLeft); break;
            case 8: MeshFromPoints(square.TopLeft, square.CentreTop, square.CentreLeft); break;
            case 9: MeshFromPoints(square.TopLeft, square.CentreTop, square.CentreBottom, square.BottomLeft); break;
            case 10: MeshFromPoints(square.TopLeft, square.CentreTop, square.CentreRight, square.BottomRight, square.CentreBottom, square.CentreLeft); break;
            case 11: MeshFromPoints(square.TopLeft, square.CentreTop, square.CentreRight, square.BottomRight, square.BottomLeft); break;
            case 12: MeshFromPoints(square.TopLeft, square.TopRight, square.CentreRight, square.CentreLeft); break;
            case 13: MeshFromPoints(square.TopLeft, square.TopRight, square.CentreRight, square.CentreBottom, square.BottomLeft); break;
            case 14: MeshFromPoints(square.TopLeft, square.TopRight, square.BottomRight, square.CentreBottom, square.CentreLeft); break;
            case 15:
                MeshFromPoints(square.TopLeft, square.TopRight, square.BottomRight, square.BottomLeft);
                _checkedVertices.Add(square.TopLeft.VertexIndex);
                _checkedVertices.Add(square.TopRight.VertexIndex);
                _checkedVertices.Add(square.BottomRight.VertexIndex);
                _checkedVertices.Add(square.BottomLeft.VertexIndex);
                break;
            default: throw new InvalidOperationException($"Unsupported square configuration {square.Configuration}.");
        }
    }

    private void MeshFromPoints(params Node[] points)
    {
        AssignVertices(points);
        if (points.Length >= 3) CreateTriangle(points[0], points[1], points[2]);
        if (points.Length >= 4) CreateTriangle(points[0], points[2], points[3]);
        if (points.Length >= 5) CreateTriangle(points[0], points[3], points[4]);
        if (points.Length >= 6) CreateTriangle(points[0], points[4], points[5]);
    }

    private void AssignVertices(IEnumerable<Node> points)
    {
        foreach (var point in points)
        {
            if (point.VertexIndex != -1) continue;
            point.VertexIndex = _vertices.Count;
            _vertices.Add(point.Position);
        }
    }

    private void CreateTriangle(Node a, Node b, Node c)
    {
        var triangle = new CaveTriangle(a.VertexIndex, b.VertexIndex, c.VertexIndex);
        _triangles.Add(triangle);
        AddTriangleToDictionary(triangle.A, triangle);
        AddTriangleToDictionary(triangle.B, triangle);
        AddTriangleToDictionary(triangle.C, triangle);
    }

    private void AddTriangleToDictionary(int vertexIndex, CaveTriangle triangle)
    {
        if (!_trianglesByVertex.TryGetValue(vertexIndex, out var triangles))
        {
            triangles = [];
            _trianglesByVertex.Add(vertexIndex, triangles);
        }
        triangles.Add(triangle);
    }

    private void CalculateMeshOutlines()
    {
        for (var vertexIndex = 0; vertexIndex < _vertices.Count; vertexIndex++)
        {
            if (_checkedVertices.Contains(vertexIndex)) continue;
            var nextVertex = GetConnectedOutlineVertex(vertexIndex);
            if (nextVertex == -1) continue;

            _checkedVertices.Add(vertexIndex);
            var outline = new List<int> { vertexIndex };
            _outlines.Add(outline);
            FollowOutline(nextVertex, outline);
            outline.Add(vertexIndex);
        }
        SimplifyMeshOutlines();
    }

    private void FollowOutline(int vertexIndex, List<int> outline)
    {
        outline.Add(vertexIndex);
        _checkedVertices.Add(vertexIndex);
        var nextVertex = GetConnectedOutlineVertex(vertexIndex);
        if (nextVertex != -1) FollowOutline(nextVertex, outline);
    }

    private int GetConnectedOutlineVertex(int vertexIndex)
    {
        foreach (var triangle in _trianglesByVertex[vertexIndex])
            for (var i = 0; i < 3; i++)
            {
                var candidate = triangle[i];
                if (candidate != vertexIndex && !_checkedVertices.Contains(candidate) && IsOutlineEdge(vertexIndex, candidate))
                    return candidate;
            }
        return -1;
    }

    private bool IsOutlineEdge(int vertexA, int vertexB)
    {
        var sharedTriangleCount = 0;
        foreach (var triangle in _trianglesByVertex[vertexA])
        {
            if (!triangle.Contains(vertexB)) continue;
            sharedTriangleCount++;
            if (sharedTriangleCount > 1) break;
        }
        return sharedTriangleCount == 1;
    }

    private void SimplifyMeshOutlines()
    {
        for (var outlineIndex = 0; outlineIndex < _outlines.Count; outlineIndex++)
        {
            var outline = _outlines[outlineIndex];
            var simplified = new List<int>();
            var oldDirection = default(CaveVertex);
            for (var i = 0; i < outline.Count; i++)
            {
                var first = _vertices[outline[i]];
                var second = _vertices[outline[(i + 1) % outline.Count]];
                var direction = new CaveVertex(first.X - second.X, first.Y - second.Y);
                if (direction == oldDirection) continue;
                oldDirection = direction;
                simplified.Add(outline[i]);
            }
            _outlines[outlineIndex] = simplified;
        }
    }

    private sealed class SquareGrid
    {
        public SquareGrid(bool[,] samples, float squareSize)
        {
            var nodeCountX = samples.GetLength(0);
            var nodeCountY = samples.GetLength(1);
            var mapWidth = nodeCountX * squareSize;
            var mapHeight = nodeCountY * squareSize;
            var controlNodes = new ControlNode[nodeCountX, nodeCountY];

            for (var x = 0; x < nodeCountX; x++)
                for (var y = 0; y < nodeCountY; y++)
                {
                    var position = new CaveVertex(
                        -mapWidth / 2f + x * squareSize + squareSize / 2f,
                        -mapHeight / 2f + y * squareSize + squareSize / 2f);
                    controlNodes[x, y] = new ControlNode(position, samples[x, y], squareSize);
                }

            Squares = new Square[nodeCountX - 1, nodeCountY - 1];
            for (var x = 0; x < nodeCountX - 1; x++)
                for (var y = 0; y < nodeCountY - 1; y++)
                    Squares[x, y] = new Square(
                        controlNodes[x, y + 1],
                        controlNodes[x + 1, y + 1],
                        controlNodes[x + 1, y],
                        controlNodes[x, y]);
        }

        public Square[,] Squares { get; }
    }

    private sealed class Square
    {
        public Square(ControlNode topLeft, ControlNode topRight, ControlNode bottomRight, ControlNode bottomLeft)
        {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
            CentreTop = topLeft.Right;
            CentreRight = bottomRight.Above;
            CentreBottom = bottomLeft.Right;
            CentreLeft = bottomLeft.Above;
            if (topLeft.Active) Configuration += 8;
            if (topRight.Active) Configuration += 4;
            if (bottomRight.Active) Configuration += 2;
            if (bottomLeft.Active) Configuration += 1;
        }

        public ControlNode TopLeft { get; }
        public ControlNode TopRight { get; }
        public ControlNode BottomRight { get; }
        public ControlNode BottomLeft { get; }
        public Node CentreTop { get; }
        public Node CentreRight { get; }
        public Node CentreBottom { get; }
        public Node CentreLeft { get; }
        public int Configuration { get; }
    }

    private class Node(CaveVertex position)
    {
        public CaveVertex Position { get; } = position;
        public int VertexIndex { get; set; } = -1;
    }

    private sealed class ControlNode : Node
    {
        public ControlNode(CaveVertex position, bool active, float squareSize) : base(position)
        {
            Active = active;
            Above = new Node(new CaveVertex(position.X, position.Y + squareSize / 2f));
            Right = new Node(new CaveVertex(position.X + squareSize / 2f, position.Y));
        }

        public bool Active { get; }
        public Node Above { get; }
        public Node Right { get; }
    }
}
