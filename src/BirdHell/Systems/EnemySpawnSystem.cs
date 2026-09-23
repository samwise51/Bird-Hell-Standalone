using System.Collections.ObjectModel;
using System.Numerics;
using BirdHell.Entities;
using BirdHell.World;

namespace BirdHell.Systems;

public sealed record EnemySpawnOptions
{
    public float CollisionSafetyMargin { get; init; } = 0.01f;
    public float PreferredMinimumEnemySpacing { get; init; } = 1.5f;
    public float PreferredMaximumEnemySpacing { get; init; } = 2f;
    public float PreferredMinimumPlayerDistance { get; init; } = 8f;
    public float PreferredPlayerDistance { get; init; } = 10f;
    public float PreferredMaximumPlayerDistance { get; init; } = 12f;
    public int HorizontalBandCount { get; init; } = 8;
    public int VerticalBandCount { get; init; } = 4;
}

public enum EnemySpawnFailureCode
{
    InvalidPlayerNavigationComponent,
    NoCollisionSafeCandidates,
    InsufficientSeparatedCapacity,
}

public sealed record EnemySpawnFailure(
    EnemySpawnFailureCode Code,
    int RequiredCount,
    int CandidateCount,
    int SelectedCount,
    int PlayerNavigationComponentId,
    float HardMinimumSeparation,
    string Message);

public sealed record EnemySpawnPlacement(
    int EnemyId,
    NavigationCell NavigationCell,
    Vector2 ColliderCenter,
    Vector2 Position,
    int MaximumSpeed,
    float PlayerDistance);

public sealed class EnemySpawnSelection
{
    internal EnemySpawnSelection(
        IReadOnlyList<EnemySpawnPlacement> placements,
        int candidateCount,
        int navigationComponentId,
        float hardMinimumSeparation,
        Vector2 caveMinimum,
        Vector2 caveMaximum,
        EnemySpawnOptions options)
    {
        Placements = Array.AsReadOnly(placements.ToArray());
        CandidateCount = candidateCount;
        NavigationComponentId = navigationComponentId;
        HardMinimumSeparation = hardMinimumSeparation;
        MinimumPairwiseSeparation = MinimumPairwiseDistance(placements.Select(item => item.ColliderCenter).ToArray());
        MinimumPlayerDistance = placements.Count == 0 ? float.PositiveInfinity : placements.Min(item => item.PlayerDistance);
        HorizontalSpan = Span(placements.Select(item => item.ColliderCenter.X));
        VerticalSpan = Span(placements.Select(item => item.ColliderCenter.Y));
        OccupiedHorizontalBands = CountOccupiedBands(
            placements.Select(item => item.ColliderCenter.X), caveMinimum.X, caveMaximum.X, options.HorizontalBandCount);
        OccupiedVerticalBands = CountOccupiedBands(
            placements.Select(item => item.ColliderCenter.Y), caveMinimum.Y, caveMaximum.Y, options.VerticalBandCount);
        AllPairsMeetPreferredMinimumSpacing = MinimumPairwiseSeparation >= options.PreferredMinimumEnemySpacing;
        EnemiesInPreferredPlayerDistanceBand = placements.Count(item =>
            item.PlayerDistance >= options.PreferredMinimumPlayerDistance &&
            item.PlayerDistance <= options.PreferredMaximumPlayerDistance);
    }

    public ReadOnlyCollection<EnemySpawnPlacement> Placements { get; }
    public int CandidateCount { get; }
    public int NavigationComponentId { get; }
    public float HardMinimumSeparation { get; }
    public float MinimumPairwiseSeparation { get; }
    public float MinimumPlayerDistance { get; }
    public float HorizontalSpan { get; }
    public float VerticalSpan { get; }
    public int OccupiedHorizontalBands { get; }
    public int OccupiedVerticalBands { get; }
    public bool AllPairsMeetPreferredMinimumSpacing { get; }
    public int EnemiesInPreferredPlayerDistanceBand { get; }

    public IReadOnlyList<Enemy> CreateEnemies() => Placements
        .Select(item => new Enemy(item.EnemyId, item.Position, item.MaximumSpeed))
        .ToArray();

    private static float MinimumPairwiseDistance(IReadOnlyList<Vector2> positions)
    {
        if (positions.Count < 2) return float.PositiveInfinity;
        var minimum = float.PositiveInfinity;
        for (var first = 0; first < positions.Count; first++)
        for (var second = first + 1; second < positions.Count; second++)
            minimum = MathF.Min(minimum, Vector2.Distance(positions[first], positions[second]));
        return minimum;
    }

    private static float Span(IEnumerable<float> values)
    {
        var array = values.ToArray();
        return array.Length == 0 ? 0f : array.Max() - array.Min();
    }

    private static int CountOccupiedBands(IEnumerable<float> values, float minimum, float maximum, int bandCount)
    {
        var array = values.ToArray();
        if (array.Length == 0) return 0;
        if (maximum <= minimum) return 1;
        return array.Select(value => Math.Clamp(
                (int)MathF.Floor((value - minimum) / (maximum - minimum) * bandCount), 0, bandCount - 1))
            .Distinct()
            .Count();
    }
}

public sealed class EnemySpawnResult
{
    private EnemySpawnResult(EnemySpawnSelection? selection, EnemySpawnFailure? failure)
    {
        Selection = selection;
        Failure = failure;
    }

    public bool IsSuccess => Selection is not null;
    public EnemySpawnSelection? Selection { get; }
    public EnemySpawnFailure? Failure { get; }

    internal static EnemySpawnResult Succeeded(EnemySpawnSelection selection) => new(selection, null);
    internal static EnemySpawnResult Failed(EnemySpawnFailure failure) => new(null, failure);
}

public sealed class EnemySpawnException : InvalidOperationException
{
    public EnemySpawnException(EnemySpawnFailure failure)
        : base(failure?.Message)
    {
        Failure = failure ?? throw new ArgumentNullException(nameof(failure));
    }

    public EnemySpawnFailure Failure { get; }
}

public static class EnemySpawnSystem
{
    public const int OriginalEnemyCount = 15;

    public static EnemySpawnResult Select(
        TerrainOccupancy occupancy,
        NavigationGrid navigationGrid,
        Player player,
        PlayerSpawnSelection playerSpawn,
        string seed,
        EnemySpawnOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(navigationGrid);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(playerSpawn);
        ArgumentNullException.ThrowIfNull(seed);
        options ??= new EnemySpawnOptions();
        ValidateOptions(options);

        var componentId = playerSpawn.NavigationComponentId;
        var hardMinimumSeparation = Enemy.OriginalColliderRadius * 2f + options.CollisionSafetyMargin;
        if (componentId == NavigationGrid.NoComponent ||
            navigationGrid.GetComponentId(playerSpawn.NavigationCell) != componentId)
            return Failed(
                EnemySpawnFailureCode.InvalidPlayerNavigationComponent,
                0,
                0,
                "The verified player navigation cell does not belong to the reported component.");

        var candidates = EnumerateCandidates(
            occupancy, navigationGrid, player, playerSpawn.NavigationCell, componentId, options.CollisionSafetyMargin);
        if (candidates.Count == 0)
            return Failed(
                EnemySpawnFailureCode.NoCollisionSafeCandidates,
                0,
                0,
                "The player's navigation component contains no collision-safe enemy positions.");

        var selected = SelectFarthestPoints(
            candidates, hardMinimumSeparation, OriginalEnemyCount, options);
        if (selected.Count != OriginalEnemyCount)
            return Failed(
                EnemySpawnFailureCode.InsufficientSeparatedCapacity,
                candidates.Count,
                selected.Count,
                $"Only {selected.Count} of {OriginalEnemyCount} required nonoverlapping enemy positions could be selected.");

        var placements = selected.Select((candidate, id) => new EnemySpawnPlacement(
                id,
                candidate.Cell,
                candidate.Center,
                candidate.Center - Enemy.OriginalColliderOffset,
                SpeedFor(seed, id),
                candidate.PlayerDistance))
            .ToArray();
        return EnemySpawnResult.Succeeded(new EnemySpawnSelection(
            placements,
            candidates.Count,
            componentId,
            hardMinimumSeparation,
            new Vector2(occupancy.Geometry.MinX, occupancy.Geometry.MinY),
            new Vector2(occupancy.Geometry.MaxX, occupancy.Geometry.MaxY),
            options));

        EnemySpawnResult Failed(
            EnemySpawnFailureCode code,
            int candidateCount,
            int selectedCount,
            string message) => EnemySpawnResult.Failed(new EnemySpawnFailure(
            code,
            OriginalEnemyCount,
            candidateCount,
            selectedCount,
            componentId,
            hardMinimumSeparation,
            message));
    }

    private static List<Candidate> EnumerateCandidates(
        TerrainOccupancy occupancy,
        NavigationGrid grid,
        Player player,
        NavigationCell playerCell,
        int componentId,
        float safetyMargin)
    {
        var candidates = new List<Candidate>();
        for (var y = 0; y < grid.Height; y++)
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = new NavigationCell(x, y);
            if (grid.GetComponentId(cell) != componentId || !grid.AreConnected(playerCell, cell)) continue;
            var center = grid.CellToWorld(cell);
            if (!occupancy.IsTraversable(center) ||
                occupancy.IntersectsCircle(center, Enemy.OriginalColliderRadius, 0f) ||
                IntersectsPlayer(center, player, safetyMargin))
                continue;
            candidates.Add(new Candidate(cell, center, Vector2.Distance(center, player.ColliderCenter)));
        }
        return candidates
            .OrderBy(candidate => candidate.Center.X)
            .ThenBy(candidate => candidate.Center.Y)
            .ThenBy(candidate => candidate.Cell.X)
            .ThenBy(candidate => candidate.Cell.Y)
            .ToList();
    }

    private static List<Candidate> SelectFarthestPoints(
        IReadOnlyList<Candidate> candidates,
        float hardMinimumSeparation,
        int count,
        EnemySpawnOptions options)
    {
        var selected = new List<Candidate>(count);
        var remaining = candidates.ToList();
        while (selected.Count < count)
        {
            var eligible = remaining
                .Where(candidate => selected.All(existing =>
                    Vector2.Distance(candidate.Center, existing.Center) + 0.0001f >= hardMinimumSeparation))
                .ToArray();
            if (eligible.Length == 0) break;
            var preferredPlayerDistanceCandidates = eligible
                .Where(candidate => candidate.PlayerDistance >= options.PreferredMinimumPlayerDistance)
                .ToArray();
            var placementPool = preferredPlayerDistanceCandidates.Length > 0
                ? preferredPlayerDistanceCandidates
                : eligible;

            Candidate next;
            if (selected.Count == 0)
            {
                next = placementPool
                    .OrderBy(candidate => MathF.Abs(candidate.PlayerDistance - options.PreferredPlayerDistance))
                    .ThenBy(candidate => candidate.Center.X)
                    .ThenBy(candidate => candidate.Center.Y)
                    .First();
            }
            else
            {
                next = placementPool
                    .Select(candidate => new
                    {
                        Candidate = candidate,
                        MinimumSelectedDistance = selected.Min(existing =>
                            Vector2.Distance(candidate.Center, existing.Center)),
                        InPreferredPlayerBand = candidate.PlayerDistance >= options.PreferredMinimumPlayerDistance &&
                                                candidate.PlayerDistance <= options.PreferredMaximumPlayerDistance,
                    })
                    .OrderByDescending(item => item.MinimumSelectedDistance)
                    .ThenByDescending(item => item.InPreferredPlayerBand)
                    .ThenBy(item => MathF.Abs(item.Candidate.PlayerDistance - options.PreferredPlayerDistance))
                    .ThenBy(item => item.Candidate.Center.X)
                    .ThenBy(item => item.Candidate.Center.Y)
                    .Select(item => item.Candidate)
                    .First();
            }
            selected.Add(next);
            remaining.Remove(next);
        }
        return selected;
    }

    private static bool IntersectsPlayer(Vector2 enemyCenter, Player player, float safetyMargin)
    {
        var coreOffset = new Vector2(0f, player.Collider.CoreHalfHeight);
        var core = new TerrainSegment(player.ColliderCenter - coreOffset, player.ColliderCenter + coreOffset);
        var required = Enemy.OriginalColliderRadius + player.Collider.Radius + safetyMargin;
        return TerrainCollisionSystem.DistanceSquaredToSegment(enemyCenter, core) < required * required;
    }

    private static int SpeedFor(string seed, int enemyId)
    {
        var hash = unchecked((uint)StableHash($"enemy-speed:v1:{seed}:{enemyId}"));
        return 2 + (int)(hash % 8u);
    }

    private static int StableHash(string value)
    {
        const uint prime = 16777619;
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return unchecked((int)hash);
    }

    private static void ValidateOptions(EnemySpawnOptions options)
    {
        if (!float.IsFinite(options.CollisionSafetyMargin) || options.CollisionSafetyMargin < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.CollisionSafetyMargin));
        if (!float.IsFinite(options.PreferredMinimumEnemySpacing) || options.PreferredMinimumEnemySpacing < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.PreferredMinimumEnemySpacing));
        if (!float.IsFinite(options.PreferredMaximumEnemySpacing) ||
            options.PreferredMaximumEnemySpacing < options.PreferredMinimumEnemySpacing)
            throw new ArgumentOutOfRangeException(nameof(options.PreferredMaximumEnemySpacing));
        if (!float.IsFinite(options.PreferredMinimumPlayerDistance) || options.PreferredMinimumPlayerDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.PreferredMinimumPlayerDistance));
        if (!float.IsFinite(options.PreferredPlayerDistance) ||
            options.PreferredPlayerDistance < options.PreferredMinimumPlayerDistance ||
            options.PreferredPlayerDistance > options.PreferredMaximumPlayerDistance)
            throw new ArgumentOutOfRangeException(nameof(options.PreferredPlayerDistance));
        if (!float.IsFinite(options.PreferredMaximumPlayerDistance) ||
            options.PreferredMaximumPlayerDistance < options.PreferredMinimumPlayerDistance)
            throw new ArgumentOutOfRangeException(nameof(options.PreferredMaximumPlayerDistance));
        if (options.HorizontalBandCount <= 0) throw new ArgumentOutOfRangeException(nameof(options.HorizontalBandCount));
        if (options.VerticalBandCount <= 0) throw new ArgumentOutOfRangeException(nameof(options.VerticalBandCount));
    }

    private sealed record Candidate(NavigationCell Cell, Vector2 Center, float PlayerDistance);
}
