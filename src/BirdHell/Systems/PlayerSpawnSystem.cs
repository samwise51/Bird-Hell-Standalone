using System.Collections.ObjectModel;
using System.Numerics;
using BirdHell.Entities;
using BirdHell.World;
using BirdHell.World.Analysis;
using BirdHell.World.Geometry;

namespace BirdHell.Systems;

public sealed record PlayerSpawnOptions
{
    public float MinimumSupportWidth { get; init; } = 1f;
    public float MinimumHeadroom { get; init; } = TerrainCollisionSystem.Skin;
    public float MaximumNavigationCellDistance { get; init; } = 2f;
    public float LocalMovementRadius { get; init; } = 4f;
    public int MinimumLocalNavigationCells { get; init; } = 12;
    public int MinimumNavigationComponentCells { get; init; } = 32;
    public int MinimumEnemyClearNavigationCells { get; init; } = EnemySpawnSystem.OriginalEnemyCount;
}

public enum PlayerSpawnRejectionReason
{
    OutsideMainLogicalComponent,
    InsufficientSupportWidth,
    InsufficientHeadroom,
    CapsuleIntersectsTerrain,
    NotGroundedOnWalkableSurface,
    MissingNavigationComponent,
    NavigationComponentMismatch,
    NavigationComponentTooSmall,
    InsufficientEnemyClearNavigationCells,
    InsufficientLocalMovementSpace,
}

public sealed record PlayerSpawnSelection(
    Vector2 Position,
    Vector2 ColliderCenter,
    Vector2 SurfacePoint,
    Vector2 GroundNormal,
    NavigationCell NavigationCell,
    int LogicalComponentId,
    int NavigationComponentId,
    int NavigationComponentCellCount,
    int EnemyClearNavigationCellCount,
    int LocalMovementCellCount,
    float SupportWidth,
    float Headroom,
    float DistanceToPreferredRegion,
    int LandingSiteRank,
    int FloorRunId);

public sealed record PlayerSpawnFailure(
    int LandingCandidateCount,
    int? MainLogicalComponentId,
    ReadOnlyDictionary<PlayerSpawnRejectionReason, int> RejectionCounts,
    string Message);

public sealed class PlayerSpawnResult
{
    private PlayerSpawnResult(PlayerSpawnSelection? selection, PlayerSpawnFailure? failure)
    {
        Selection = selection;
        Failure = failure;
    }

    public bool IsSuccess => Selection is not null;
    public PlayerSpawnSelection? Selection { get; }
    public PlayerSpawnFailure? Failure { get; }

    internal static PlayerSpawnResult Succeeded(PlayerSpawnSelection selection) => new(selection, null);
    internal static PlayerSpawnResult Failed(PlayerSpawnFailure failure) => new(null, failure);
}

public sealed class PlayerSpawnException : InvalidOperationException
{
    public PlayerSpawnException(PlayerSpawnFailure failure)
        : base(failure?.Message)
    {
        Failure = failure ?? throw new ArgumentNullException(nameof(failure));
    }

    public PlayerSpawnFailure Failure { get; }
}

public static class PlayerSpawnSystem
{
    public static PlayerSpawnResult Select(
        CaveTopologyReport topology,
        CaveGeometry geometry,
        TerrainOccupancy occupancy,
        TerrainCollisionSystem collision,
        NavigationGrid navigationGrid,
        PlayerCollider collider,
        PlayerSpawnOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(navigationGrid);
        options ??= new PlayerSpawnOptions();
        ValidateOptions(options);

        var rejectionCounts = Enum.GetValues<PlayerSpawnRejectionReason>()
            .ToDictionary(reason => reason, _ => 0);
        var mainLogicalComponentId = topology.MainLogicalComponent?.Id;
        if (mainLogicalComponentId is null)
            return Failure("No main logical open-space component exists.");

        var navigationComponents = topology.NavigationComponents.ToDictionary(component => component.Id);
        var preferred = OriginalPreferredRegion(geometry);
        var survivors = new List<PlayerSpawnSelection>();

        foreach (var candidate in topology.LandingSites)
        {
            if (candidate.LogicalComponentId != mainLogicalComponentId.Value)
            {
                Reject(PlayerSpawnRejectionReason.OutsideMainLogicalComponent);
                continue;
            }
            if (candidate.SupportWidth + 0.0001f < options.MinimumSupportWidth)
            {
                Reject(PlayerSpawnRejectionReason.InsufficientSupportWidth);
                continue;
            }
            if (candidate.Headroom + 0.0001f < options.MinimumHeadroom)
            {
                Reject(PlayerSpawnRejectionReason.InsufficientHeadroom);
                continue;
            }
            if (collision.Intersects(candidate.PlayerPosition, collider))
            {
                Reject(PlayerSpawnRejectionReason.CapsuleIntersectsTerrain);
                continue;
            }

            var groundedProbe = new Player(candidate.PlayerPosition, collider);
            collision.RefreshContacts(groundedProbe);
            if (!groundedProbe.IsGrounded ||
                groundedProbe.GroundNormal.Y + 0.0001f < TerrainCollisionSystem.MinimumGroundNormalY ||
                candidate.GroundNormal.Y + 0.0001f < TerrainCollisionSystem.MinimumGroundNormalY)
            {
                Reject(PlayerSpawnRejectionReason.NotGroundedOnWalkableSurface);
                continue;
            }

            if (candidate.NavigationComponentId == NavigationGrid.NoComponent ||
                !navigationComponents.TryGetValue(candidate.NavigationComponentId, out var component))
            {
                Reject(PlayerSpawnRejectionReason.MissingNavigationComponent);
                continue;
            }
            if (!navigationGrid.TryFindNearestWalkableInComponent(
                    candidate.ColliderCenter,
                    options.MaximumNavigationCellDistance,
                    candidate.NavigationComponentId,
                    out var navigationCell) ||
                !navigationGrid.TryGetComponentForPosition(
                    candidate.ColliderCenter,
                    options.MaximumNavigationCellDistance,
                    out var nearestComponentId,
                    out var nearestCell) ||
                nearestComponentId != candidate.NavigationComponentId ||
                navigationGrid.GetComponentId(navigationCell) != candidate.NavigationComponentId ||
                navigationCell != nearestCell ||
                !occupancy.IsTraversable(navigationGrid.CellToWorld(navigationCell)))
            {
                Reject(PlayerSpawnRejectionReason.NavigationComponentMismatch);
                continue;
            }
            if (component.CellCount < options.MinimumNavigationComponentCells)
            {
                Reject(PlayerSpawnRejectionReason.NavigationComponentTooSmall);
                continue;
            }
            if (component.EnemyClearCellCount < options.MinimumEnemyClearNavigationCells)
            {
                Reject(PlayerSpawnRejectionReason.InsufficientEnemyClearNavigationCells);
                continue;
            }

            var localMovementCells = CountLocalMovementCells(
                navigationGrid, navigationCell, candidate.NavigationComponentId, options.LocalMovementRadius);
            if (localMovementCells < options.MinimumLocalNavigationCells)
            {
                Reject(PlayerSpawnRejectionReason.InsufficientLocalMovementSpace);
                continue;
            }

            survivors.Add(new PlayerSpawnSelection(
                candidate.PlayerPosition,
                candidate.ColliderCenter,
                candidate.SurfacePoint,
                groundedProbe.GroundNormal,
                navigationCell,
                candidate.LogicalComponentId,
                candidate.NavigationComponentId,
                component.CellCount,
                component.EnemyClearCellCount,
                localMovementCells,
                candidate.SupportWidth,
                candidate.Headroom,
                Vector2.Distance(candidate.PlayerPosition, preferred),
                candidate.Rank,
                candidate.FloorRunId));
        }

        var selected = survivors
            .OrderByDescending(candidate => candidate.EnemyClearNavigationCellCount)
            .ThenByDescending(candidate => candidate.NavigationComponentCellCount)
            .ThenByDescending(candidate => candidate.LocalMovementCellCount)
            .ThenByDescending(candidate => candidate.SupportWidth)
            .ThenByDescending(candidate => candidate.Headroom)
            .ThenBy(candidate => candidate.DistanceToPreferredRegion)
            .ThenBy(candidate => candidate.Position.X)
            .ThenBy(candidate => candidate.Position.Y)
            .ThenBy(candidate => candidate.FloorRunId)
            .ThenBy(candidate => candidate.LandingSiteRank)
            .FirstOrDefault();
        return selected is not null
            ? PlayerSpawnResult.Succeeded(selected)
            : Failure("No topology landing candidate satisfies all player spawn safety requirements.");

        void Reject(PlayerSpawnRejectionReason reason) => rejectionCounts[reason]++;

        PlayerSpawnResult Failure(string message) => PlayerSpawnResult.Failed(new PlayerSpawnFailure(
            topology.LandingSites.Count,
            mainLogicalComponentId,
            new ReadOnlyDictionary<PlayerSpawnRejectionReason, int>(rejectionCounts),
            message));
    }

    private static int CountLocalMovementCells(
        NavigationGrid grid,
        NavigationCell start,
        int componentId,
        float radius)
    {
        var startCenter = grid.CellToWorld(start);
        var radiusSquared = radius * radius;
        var visited = new HashSet<NavigationCell> { start };
        var queue = new Queue<NavigationCell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in grid.GetNeighbors(current))
            {
                if (grid.GetComponentId(neighbor) != componentId || visited.Contains(neighbor) ||
                    Vector2.DistanceSquared(startCenter, grid.CellToWorld(neighbor)) > radiusSquared)
                    continue;
                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }
        return visited.Count;
    }

    private static Vector2 OriginalPreferredRegion(CaveGeometry geometry)
    {
        var primaryOutline = geometry.Outlines
            .OrderByDescending(outline => BoundsArea(outline.Points))
            .FirstOrDefault();
        if (primaryOutline is null) return new Vector2((geometry.MinX + geometry.MaxX) / 2f - 5f,
            (geometry.MinY + geometry.MaxY) / 2f - 5f);
        return new Vector2(
            (primaryOutline.Points.Min(point => point.X) + primaryOutline.Points.Max(point => point.X)) / 2f - 5f,
            (primaryOutline.Points.Min(point => point.Y) + primaryOutline.Points.Max(point => point.Y)) / 2f - 5f);
    }

    private static float BoundsArea(IReadOnlyCollection<CaveVertex> points) =>
        (points.Max(point => point.X) - points.Min(point => point.X)) *
        (points.Max(point => point.Y) - points.Min(point => point.Y));

    private static void ValidateOptions(PlayerSpawnOptions options)
    {
        if (!float.IsFinite(options.MinimumSupportWidth) || options.MinimumSupportWidth < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumSupportWidth));
        if (!float.IsFinite(options.MinimumHeadroom) || options.MinimumHeadroom < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumHeadroom));
        if (!float.IsFinite(options.MaximumNavigationCellDistance) || options.MaximumNavigationCellDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumNavigationCellDistance));
        if (!float.IsFinite(options.LocalMovementRadius) || options.LocalMovementRadius < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.LocalMovementRadius));
        if (options.MinimumLocalNavigationCells < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumLocalNavigationCells));
        if (options.MinimumNavigationComponentCells < 1)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumNavigationComponentCells));
        if (options.MinimumEnemyClearNavigationCells < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumEnemyClearNavigationCells));
    }
}
