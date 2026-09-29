using System.Numerics;
using BirdHell.World;

namespace BirdHell.Systems;

public sealed class AStarPathfinder
{
    public const float MaximumNearestNodeDistance = 100f;
    private readonly NavigationGrid _grid;

    public AStarPathfinder(NavigationGrid grid) => _grid = grid ?? throw new ArgumentNullException(nameof(grid));
    public NavigationGrid Grid => _grid;

    public IReadOnlyList<Vector2>? FindPath(Vector2 start, Vector2 end)
    {
        if (!_grid.IsWorldPositionInTraversableSpace(start) || !_grid.IsWorldPositionInTraversableSpace(end))
            return null;
        if (!_grid.TryFindNearestWalkable(start, MaximumNearestNodeDistance, out var startCell) ||
            !_grid.TryFindNearestWalkable(end, MaximumNearestNodeDistance, out var endCell))
            return null;
        return FindPath(startCell, endCell);
    }

    public IReadOnlyList<Vector2>? FindPath(NavigationCell startCell, NavigationCell endCell)
    {
        if (!_grid.AreConnected(startCell, endCell)) return null;

        var count = _grid.CellCount;
        var costs = Enumerable.Repeat(float.PositiveInfinity, count).ToArray();
        var parents = Enumerable.Repeat(-1, count).ToArray();
        var closed = new bool[count];
        var open = new PriorityQueue<int, (float Total, float Heuristic, int Index)>();
        var startIndex = startCell.ToIndex(_grid.Width);
        var endIndex = endCell.ToIndex(_grid.Width);
        costs[startIndex] = 0f;
        var initialHeuristic = Heuristic(startCell, endCell);
        open.Enqueue(startIndex, (initialHeuristic, initialHeuristic, startIndex));

        while (open.TryDequeue(out var currentIndex, out _))
        {
            if (closed[currentIndex]) continue;
            closed[currentIndex] = true;
            if (currentIndex == endIndex) return Reconstruct(parents, endIndex);
            var current = ToCell(currentIndex);
            foreach (var neighbor in _grid.GetNeighbors(current))
            {
                var neighborIndex = neighbor.ToIndex(_grid.Width);
                if (closed[neighborIndex]) continue;
                var diagonal = neighbor.X != current.X && neighbor.Y != current.Y;
                var candidate = costs[currentIndex] + (diagonal ? 1.41421356237f : 1f) * _grid.NodeSize;
                if (candidate >= costs[neighborIndex]) continue;
                costs[neighborIndex] = candidate;
                parents[neighborIndex] = currentIndex;
                var heuristic = Heuristic(neighbor, endCell);
                open.Enqueue(neighborIndex, (candidate + heuristic, heuristic, neighborIndex));
            }
        }
        return null;
    }

    private IReadOnlyList<Vector2> Reconstruct(int[] parents, int endIndex)
    {
        var path = new List<Vector2>();
        for (var index = endIndex; index >= 0; index = parents[index])
        {
            path.Add(_grid.CellToWorld(ToCell(index)));
            if (parents[index] < 0) break;
        }
        path.Reverse();
        return path;
    }

    private NavigationCell ToCell(int index) => new(index % _grid.Width, index / _grid.Width);

    private float Heuristic(NavigationCell first, NavigationCell second)
    {
        var dx = Math.Abs(first.X - second.X);
        var dy = Math.Abs(first.Y - second.Y);
        return (Math.Max(dx, dy) + 0.41421356237f * Math.Min(dx, dy)) * _grid.NodeSize;
    }
}
