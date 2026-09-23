using System.Collections.ObjectModel;
using System.Numerics;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World.Geometry;

namespace BirdHell.World.Analysis;

public sealed record CaveTopologyAnalysisOptions
{
    public float LandingSampleSpacing { get; init; } = 0.25f;
    public float MinimumLandingSupportWidth { get; init; } = PlayerCollider.Original.Width;
    public int HorizontalBandCount { get; init; } = 8;
    public int ElevationBandCount { get; init; } = 8;
    public float ApproximateBottleneckClearance { get; init; } = 1.5f;
    public float LandingNavigationSearchDistance { get; init; } = 2f;
    public CaveQualityThresholds Thresholds { get; init; } = new();
}

public sealed record CaveQualityThresholds
{
    public double? MinimumOpenSpacePercentage { get; init; }
    public double? MaximumOpenSpacePercentage { get; init; }
    public double? MinimumMainLogicalComponentShare { get; init; }
    public double? MinimumMainHorizontalCoverage { get; init; }
    public double? MinimumMainVerticalCoverage { get; init; }
    public int? MinimumLandingSiteCount { get; init; }
    public int? MinimumPlayerClearLogicalTileCenters { get; init; }
    public int? MinimumEnemyValidNavigationCells { get; init; }
}

public sealed record LogicalComponentReport(
    int Id,
    int TileCount,
    int MinX,
    int MaxX,
    int MinY,
    int MaxY,
    double HorizontalCoverage,
    double VerticalCoverage);

public sealed record FloorSurfaceReport(
    int Id,
    int OutlineIndex,
    Vector2 Start,
    Vector2 End,
    Vector2 OpenNormal,
    float Length,
    float SlopeDegrees,
    bool IsWalkable);

public sealed record FloorRunReport(
    int Id,
    int OutlineIndex,
    ReadOnlyCollection<int> SurfaceIds,
    float SurfaceLength,
    float HorizontalSpan,
    float MinimumY,
    float MaximumY);

public sealed record LandingSiteReport(
    int Rank,
    Vector2 PlayerPosition,
    Vector2 ColliderCenter,
    Vector2 SurfacePoint,
    Vector2 GroundNormal,
    int FloorRunId,
    int LogicalComponentId,
    int NavigationComponentId,
    float SupportWidth,
    float Headroom,
    int HorizontalBand,
    int ElevationBand);

public sealed record NavigationComponentReport(
    int Id,
    int CellCount,
    int EnemyClearCellCount,
    Vector2 MinimumCenter,
    Vector2 MaximumCenter);

public enum CaveValidationFailureCode
{
    NoOpenSpace,
    NoLogicalComponent,
    NoPlayerCapsuleClearLocation,
    NoGroundedLandingSite,
    NoEnemyClearNavigationCell,
    OpenSpaceBelowMinimum,
    OpenSpaceAboveMaximum,
    MainLogicalShareBelowMinimum,
    MainHorizontalCoverageBelowMinimum,
    MainVerticalCoverageBelowMinimum,
    LandingSiteCountBelowMinimum,
    PlayerClearLocationCountBelowMinimum,
    EnemyValidCellCountBelowMinimum,
}

public sealed record CaveValidationFailure(
    CaveValidationFailureCode Code,
    string Message,
    double Observed,
    double Required);

public sealed class CaveTopologyReport
{
    internal CaveTopologyReport(
        int totalTileCount,
        int openTileCount,
        IReadOnlyList<LogicalComponentReport> logicalComponents,
        LogicalComponentReport? mainLogicalComponent,
        IReadOnlyList<FloorSurfaceReport> floorSurfaces,
        IReadOnlyList<FloorRunReport> floorRuns,
        IReadOnlyList<LandingSiteReport> landingSites,
        int playerClearLogicalTileCenterCount,
        int playerClearNavigationCellCount,
        IReadOnlyList<NavigationComponentReport> navigationComponents,
        int enemyValidNavigationCellCount,
        int approximateBottleneckCellCount,
        IReadOnlyList<int> landingSitesByHorizontalBand,
        IReadOnlyList<int> landingSitesByElevationBand,
        IReadOnlyList<CaveValidationFailure> hardValidationFailures)
    {
        TotalTileCount = totalTileCount;
        OpenTileCount = openTileCount;
        OpenSpacePercentage = totalTileCount == 0 ? 0d : openTileCount * 100d / totalTileCount;
        LogicalComponents = Array.AsReadOnly(logicalComponents.ToArray());
        MainLogicalComponent = mainLogicalComponent;
        MainLogicalComponentShare = openTileCount == 0 || mainLogicalComponent is null
            ? 0d
            : mainLogicalComponent.TileCount / (double)openTileCount;
        FloorSurfaces = Array.AsReadOnly(floorSurfaces.ToArray());
        FloorRuns = Array.AsReadOnly(floorRuns.ToArray());
        LandingSites = Array.AsReadOnly(landingSites.ToArray());
        PlayerClearLogicalTileCenterCount = playerClearLogicalTileCenterCount;
        PlayerClearNavigationCellCount = playerClearNavigationCellCount;
        NavigationComponents = Array.AsReadOnly(navigationComponents.ToArray());
        EnemyValidNavigationCellCount = enemyValidNavigationCellCount;
        ApproximateBottleneckCellCount = approximateBottleneckCellCount;
        LandingSitesByHorizontalBand = Array.AsReadOnly(landingSitesByHorizontalBand.ToArray());
        LandingSitesByElevationBand = Array.AsReadOnly(landingSitesByElevationBand.ToArray());
        HardValidationFailures = Array.AsReadOnly(hardValidationFailures.ToArray());
    }

    public int TotalTileCount { get; }
    public int OpenTileCount { get; }
    public double OpenSpacePercentage { get; }
    public ReadOnlyCollection<LogicalComponentReport> LogicalComponents { get; }
    public LogicalComponentReport? MainLogicalComponent { get; }
    public double MainLogicalComponentShare { get; }
    public ReadOnlyCollection<FloorSurfaceReport> FloorSurfaces { get; }
    public ReadOnlyCollection<FloorRunReport> FloorRuns { get; }
    public ReadOnlyCollection<LandingSiteReport> LandingSites { get; }
    public int PlayerClearLogicalTileCenterCount { get; }
    public int PlayerClearNavigationCellCount { get; }
    public ReadOnlyCollection<NavigationComponentReport> NavigationComponents { get; }
    public int EnemyValidNavigationCellCount { get; }
    public int ApproximateBottleneckCellCount { get; }
    public ReadOnlyCollection<int> LandingSitesByHorizontalBand { get; }
    public ReadOnlyCollection<int> LandingSitesByElevationBand { get; }
    public ReadOnlyCollection<CaveValidationFailure> HardValidationFailures { get; }
    public bool IsHardValid => HardValidationFailures.Count == 0;
}

public sealed class CaveTopologyAnalyzer
{
    private static readonly (int X, int Y)[] LogicalNeighborOffsets = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    public CaveTopologyReport Analyze(
        CaveMap map,
        CaveGeometry geometry,
        TerrainOccupancy? occupancy = null,
        NavigationGrid? navigationGrid = null,
        CaveTopologyAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(geometry);
        options ??= new CaveTopologyAnalysisOptions();
        ValidateInputs(map, geometry, options);
        occupancy ??= new TerrainOccupancy(geometry);
        navigationGrid ??= new NavigationGrid(occupancy);
        var collision = new TerrainCollisionSystem(occupancy);

        var (logicalComponents, componentIds, openTileCount) = AnalyzeLogicalComponents(map);
        var mainLogicalComponent = logicalComponents
            .OrderByDescending(component => component.TileCount)
            .ThenBy(component => component.Id)
            .FirstOrDefault();
        var floorSurfaces = AnalyzeFloorSurfaces(geometry, occupancy);
        var floorRuns = BuildFloorRuns(floorSurfaces);
        var playerClearLogicalTileCenters = CountPlayerClearLogicalTileCenters(
            map, geometry, collision, PlayerCollider.Original);
        var playerClearNavigationCells = CountPlayerClearNavigationCells(
            navigationGrid, collision, PlayerCollider.Original);
        var landingSites = AnalyzeLandingSites(
            map, geometry, occupancy, collision, navigationGrid, floorSurfaces, floorRuns,
            componentIds, options);
        var navigationComponents = AnalyzeNavigationComponents(navigationGrid, occupancy);
        var enemyValidNavigationCells = navigationComponents.Sum(component => component.EnemyClearCellCount);
        var bottleneckCells = CountApproximateBottlenecks(navigationGrid, occupancy, options);
        var horizontalDistribution = CountBands(landingSites.Select(site => site.HorizontalBand), options.HorizontalBandCount);
        var elevationDistribution = CountBands(landingSites.Select(site => site.ElevationBand), options.ElevationBandCount);
        var failures = Validate(
            map.Width * map.Height,
            openTileCount,
            logicalComponents,
            mainLogicalComponent,
            playerClearLogicalTileCenters,
            landingSites.Count,
            enemyValidNavigationCells,
            options.Thresholds);

        return new CaveTopologyReport(
            map.Width * map.Height,
            openTileCount,
            logicalComponents,
            mainLogicalComponent,
            floorSurfaces,
            floorRuns,
            landingSites,
            playerClearLogicalTileCenters,
            playerClearNavigationCells,
            navigationComponents,
            enemyValidNavigationCells,
            bottleneckCells,
            horizontalDistribution,
            elevationDistribution,
            failures);
    }

    private static (List<LogicalComponentReport> Components, int[,] ComponentIds, int OpenTileCount)
        AnalyzeLogicalComponents(CaveMap map)
    {
        var componentIds = new int[map.Width, map.Height];
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
            componentIds[x, y] = -1;
        var components = new List<LogicalComponentReport>();
        var openCount = 0;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            if (map[x, y] != CaveTile.Open || componentIds[x, y] >= 0) continue;
            var id = components.Count;
            var queue = new Queue<GridCoordinate>();
            queue.Enqueue(new GridCoordinate(x, y));
            componentIds[x, y] = id;
            var count = 0;
            var minX = x;
            var maxX = x;
            var minY = y;
            var maxY = y;
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                count++;
                minX = Math.Min(minX, tile.X);
                maxX = Math.Max(maxX, tile.X);
                minY = Math.Min(minY, tile.Y);
                maxY = Math.Max(maxY, tile.Y);
                foreach (var offset in LogicalNeighborOffsets)
                {
                    var neighborX = tile.X + offset.X;
                    var neighborY = tile.Y + offset.Y;
                    if (!map.IsInBounds(neighborX, neighborY) ||
                        map[neighborX, neighborY] != CaveTile.Open || componentIds[neighborX, neighborY] >= 0)
                        continue;
                    componentIds[neighborX, neighborY] = id;
                    queue.Enqueue(new GridCoordinate(neighborX, neighborY));
                }
            }
            openCount += count;
            components.Add(new LogicalComponentReport(
                id, count, minX, maxX, minY, maxY,
                (maxX - minX + 1d) / map.Width,
                (maxY - minY + 1d) / map.Height));
        }
        return (components, componentIds, openCount);
    }

    private static List<FloorSurfaceReport> AnalyzeFloorSurfaces(CaveGeometry geometry, TerrainOccupancy occupancy)
    {
        var result = new List<FloorSurfaceReport>();
        var id = 0;
        for (var outlineIndex = 0; outlineIndex < geometry.Outlines.Count; outlineIndex++)
        {
            var outline = geometry.Outlines[outlineIndex];
            for (var pointIndex = 0; pointIndex < outline.Points.Count - 1; pointIndex++)
            {
                var start = ToVector(outline.Points[pointIndex]);
                var end = ToVector(outline.Points[pointIndex + 1]);
                var edge = end - start;
                var length = edge.Length();
                if (length <= 0.000001f) continue;
                var candidateNormal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
                var midpoint = (start + end) / 2f;
                var probe = MathF.Min(0.05f, geometry.SquareSize * 0.1f);
                var plusOpen = occupancy.IsTraversable(midpoint + candidateNormal * probe);
                var minusOpen = occupancy.IsTraversable(midpoint - candidateNormal * probe);
                if (plusOpen == minusOpen) continue;
                var openNormal = plusOpen ? candidateNormal : -candidateNormal;
                if (openNormal.Y <= 0f) continue;
                var slopeDegrees = MathF.Acos(Math.Clamp(openNormal.Y, -1f, 1f)) * 180f / MathF.PI;
                result.Add(new FloorSurfaceReport(
                    id++, outlineIndex, start, end, openNormal, length, slopeDegrees,
                    openNormal.Y >= TerrainCollisionSystem.MinimumGroundNormalY - 0.0001f));
            }
        }
        return result;
    }

    private static List<FloorRunReport> BuildFloorRuns(IReadOnlyList<FloorSurfaceReport> surfaces)
    {
        var runs = new List<FloorRunReport>();
        foreach (var outlineGroup in surfaces.Where(surface => surface.IsWalkable)
                     .GroupBy(surface => surface.OutlineIndex).OrderBy(group => group.Key))
        {
            var ordered = outlineGroup.OrderBy(surface => surface.Id).ToArray();
            var current = new List<FloorSurfaceReport>();
            foreach (var surface in ordered)
            {
                if (current.Count > 0 && Vector2.DistanceSquared(current[^1].End, surface.Start) > 0.000001f)
                {
                    AddRun(current);
                    current.Clear();
                }
                current.Add(surface);
            }
            AddRun(current);
        }
        return runs;

        void AddRun(IReadOnlyList<FloorSurfaceReport> runSurfaces)
        {
            if (runSurfaces.Count == 0) return;
            var points = runSurfaces.SelectMany(surface => new[] { surface.Start, surface.End }).ToArray();
            runs.Add(new FloorRunReport(
                runs.Count,
                runSurfaces[0].OutlineIndex,
                Array.AsReadOnly(runSurfaces.Select(surface => surface.Id).ToArray()),
                runSurfaces.Sum(surface => surface.Length),
                points.Max(point => point.X) - points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.Y)));
        }
    }

    private static List<LandingSiteReport> AnalyzeLandingSites(
        CaveMap map,
        CaveGeometry geometry,
        TerrainOccupancy occupancy,
        TerrainCollisionSystem collision,
        NavigationGrid navigationGrid,
        IReadOnlyList<FloorSurfaceReport> surfaces,
        IReadOnlyList<FloorRunReport> runs,
        int[,] logicalComponentIds,
        CaveTopologyAnalysisOptions options)
    {
        var collider = PlayerCollider.Original;
        var candidates = new List<LandingSiteReport>();
        foreach (var run in runs.Where(run => run.HorizontalSpan >= options.MinimumLandingSupportWidth))
        {
            var runSurfaces = run.SurfaceIds.Select(id => surfaces[id]).ToArray();
            var firstDistance = collider.Radius;
            var lastDistance = run.SurfaceLength - collider.Radius;
            for (var distance = firstDistance; distance <= lastDistance + 0.0001f; distance += options.LandingSampleSpacing)
            {
                var (surface, point) = PointAlongRun(runSurfaces, MathF.Min(distance, run.SurfaceLength));
                var lowerCore = point + surface.OpenNormal * (collider.Radius + TerrainCollisionSystem.Skin);
                var center = lowerCore + new Vector2(0f, collider.CoreHalfHeight);
                var playerPosition = center - collider.Offset;
                if (collision.Intersects(playerPosition, collider, TerrainCollisionSystem.Skin * 0.25f)) continue;
                if (!occupancy.IsTraversable(center)) continue;
                var groundedProbe = new Player(playerPosition, collider);
                collision.RefreshContacts(groundedProbe);
                if (!groundedProbe.IsGrounded) continue;
                var logicalComponent = TryWorldToLogicalTile(map, geometry, center, out var tile)
                    ? logicalComponentIds[tile.X, tile.Y]
                    : -1;
                var navigationComponent = navigationGrid.TryGetComponentForPosition(
                    center, options.LandingNavigationSearchDistance, out var componentId, out _)
                    ? componentId
                    : NavigationGrid.NoComponent;
                var headroom = MeasureHeadroom(center, collider, occupancy, geometry);
                var horizontalBand = ToBand(point.X, geometry.MinX, geometry.MaxX, options.HorizontalBandCount);
                var elevationBand = ToBand(point.Y, geometry.MinY, geometry.MaxY, options.ElevationBandCount);
                candidates.Add(new LandingSiteReport(
                    0, playerPosition, center, point, groundedProbe.GroundNormal, run.Id, logicalComponent,
                    navigationComponent, run.HorizontalSpan, headroom, horizontalBand, elevationBand));
            }
        }

        var ordered = candidates
            .OrderByDescending(site => site.SupportWidth)
            .ThenByDescending(site => site.Headroom)
            .ThenBy(site => site.SurfacePoint.X)
            .ThenBy(site => site.SurfacePoint.Y)
            .ThenBy(site => site.FloorRunId)
            .Select((site, rank) => site with { Rank = rank })
            .ToList();
        return ordered;
    }

    private static (FloorSurfaceReport Surface, Vector2 Point) PointAlongRun(
        IReadOnlyList<FloorSurfaceReport> surfaces,
        float distance)
    {
        foreach (var surface in surfaces)
        {
            if (distance <= surface.Length)
                return (surface, Vector2.Lerp(surface.Start, surface.End, distance / surface.Length));
            distance -= surface.Length;
        }
        return (surfaces[^1], surfaces[^1].End);
    }

    private static int CountPlayerClearLogicalTileCenters(
        CaveMap map,
        CaveGeometry geometry,
        TerrainCollisionSystem collision,
        PlayerCollider collider)
    {
        var count = 0;
        for (var x = 0; x < map.Width; x++)
        for (var y = 0; y < map.Height; y++)
        {
            if (map[x, y] != CaveTile.Open) continue;
            var center = LogicalTileCenter(geometry, x, y);
            if (!collision.Intersects(center - collider.Offset, collider)) count++;
        }
        return count;
    }

    private static int CountPlayerClearNavigationCells(
        NavigationGrid grid,
        TerrainCollisionSystem collision,
        PlayerCollider collider)
    {
        var count = 0;
        for (var y = 0; y < grid.Height; y++)
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = new NavigationCell(x, y);
            if (!grid.IsWalkable(cell)) continue;
            var center = grid.CellToWorld(cell);
            if (!collision.Intersects(center - collider.Offset, collider)) count++;
        }
        return count;
    }

    private static List<NavigationComponentReport> AnalyzeNavigationComponents(
        NavigationGrid grid,
        TerrainOccupancy occupancy)
    {
        var accumulators = Enumerable.Range(0, grid.ComponentCount)
            .Select(id => new NavigationAccumulator(id)).ToArray();
        for (var y = 0; y < grid.Height; y++)
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = new NavigationCell(x, y);
            var id = grid.GetComponentId(cell);
            if (id == NavigationGrid.NoComponent) continue;
            var center = grid.CellToWorld(cell);
            accumulators[id].Add(center, !occupancy.IntersectsCircle(center, Enemy.OriginalColliderRadius));
        }
        return accumulators.Select(accumulator => accumulator.ToReport()).ToList();
    }

    private static int CountApproximateBottlenecks(
        NavigationGrid grid,
        TerrainOccupancy occupancy,
        CaveTopologyAnalysisOptions options)
    {
        var count = 0;
        for (var y = 0; y < grid.Height; y++)
        for (var x = 0; x < grid.Width; x++)
        {
            var cell = new NavigationCell(x, y);
            if (!grid.IsWalkable(cell)) continue;
            var neighborCount = grid.GetNeighbors(cell).Count;
            if (neighborCount is >= 2 and <= 4 &&
                occupancy.GetBoundaryClearance(grid.CellToWorld(cell)) <= options.ApproximateBottleneckClearance)
                count++;
        }
        return count;
    }

    private static float MeasureHeadroom(
        Vector2 center,
        PlayerCollider collider,
        TerrainOccupancy occupancy,
        CaveGeometry geometry)
    {
        var top = center.Y + collider.CoreHalfHeight + collider.Radius;
        var ceiling = geometry.MaxY;
        foreach (var segment in occupancy.BoundarySegments)
        {
            var minX = MathF.Min(segment.A.X, segment.B.X);
            var maxX = MathF.Max(segment.A.X, segment.B.X);
            if (center.X < minX || center.X > maxX || MathF.Abs(segment.B.X - segment.A.X) < 0.000001f)
                continue;
            var t = (center.X - segment.A.X) / (segment.B.X - segment.A.X);
            if (t < 0f || t > 1f) continue;
            var y = segment.A.Y + (segment.B.Y - segment.A.Y) * t;
            if (y >= top) ceiling = MathF.Min(ceiling, y);
        }
        return MathF.Max(0f, ceiling - top);
    }

    private static bool TryWorldToLogicalTile(
        CaveMap map,
        CaveGeometry geometry,
        Vector2 world,
        out GridCoordinate tile)
    {
        var x = (int)MathF.Floor((world.X - geometry.MinX - geometry.SquareSize * 0.5f) / geometry.SquareSize);
        var y = (int)MathF.Floor((world.Y - geometry.MinY - geometry.SquareSize * 0.5f) / geometry.SquareSize);
        tile = new GridCoordinate(x, y);
        return map.IsInBounds(x, y) && map[x, y] == CaveTile.Open;
    }

    private static Vector2 LogicalTileCenter(CaveGeometry geometry, int x, int y) => new(
        geometry.MinX + (x + CaveGeometryBuilder.BorderSize) * geometry.SquareSize,
        geometry.MinY + (y + CaveGeometryBuilder.BorderSize) * geometry.SquareSize);

    private static int ToBand(float value, float minimum, float maximum, int count)
    {
        if (maximum <= minimum) return 0;
        return Math.Clamp((int)MathF.Floor((value - minimum) / (maximum - minimum) * count), 0, count - 1);
    }

    private static int[] CountBands(IEnumerable<int> bands, int count)
    {
        var result = new int[count];
        foreach (var band in bands) result[band]++;
        return result;
    }

    private static List<CaveValidationFailure> Validate(
        int totalTileCount,
        int openTileCount,
        IReadOnlyList<LogicalComponentReport> components,
        LogicalComponentReport? main,
        int playerClearLocations,
        int landingSites,
        int enemyValidCells,
        CaveQualityThresholds thresholds)
    {
        var failures = new List<CaveValidationFailure>();
        var openPercentage = openTileCount * 100d / totalTileCount;
        var mainShare = main is null || openTileCount == 0 ? 0d : main.TileCount / (double)openTileCount;
        if (openTileCount == 0) Add(CaveValidationFailureCode.NoOpenSpace, "The map contains no logical open tiles.", 0, 1);
        if (components.Count == 0) Add(CaveValidationFailureCode.NoLogicalComponent, "The map contains no connected logical open-space component.", 0, 1);
        if (playerClearLocations == 0) Add(CaveValidationFailureCode.NoPlayerCapsuleClearLocation, "No open logical tile center can contain the original player capsule.", 0, 1);
        if (landingSites == 0) Add(CaveValidationFailureCode.NoGroundedLandingSite, "No walkable floor run provides a collision-free grounded player landing candidate.", 0, 1);
        if (enemyValidCells == 0) Add(CaveValidationFailureCode.NoEnemyClearNavigationCell, "No navigation cell has clearance for the original enemy circle.", 0, 1);
        if (thresholds.MinimumOpenSpacePercentage is { } minOpen && openPercentage < minOpen)
            Add(CaveValidationFailureCode.OpenSpaceBelowMinimum, "Open-space percentage is below the configured provisional minimum.", openPercentage, minOpen);
        if (thresholds.MaximumOpenSpacePercentage is { } maxOpen && openPercentage > maxOpen)
            Add(CaveValidationFailureCode.OpenSpaceAboveMaximum, "Open-space percentage exceeds the configured provisional maximum.", openPercentage, maxOpen);
        if (thresholds.MinimumMainLogicalComponentShare is { } minShare && mainShare < minShare)
            Add(CaveValidationFailureCode.MainLogicalShareBelowMinimum, "The main logical component share is below the configured provisional minimum.", mainShare, minShare);
        if (thresholds.MinimumMainHorizontalCoverage is { } minHorizontal && (main?.HorizontalCoverage ?? 0d) < minHorizontal)
            Add(CaveValidationFailureCode.MainHorizontalCoverageBelowMinimum, "The main logical component horizontal coverage is below the configured provisional minimum.", main?.HorizontalCoverage ?? 0d, minHorizontal);
        if (thresholds.MinimumMainVerticalCoverage is { } minVertical && (main?.VerticalCoverage ?? 0d) < minVertical)
            Add(CaveValidationFailureCode.MainVerticalCoverageBelowMinimum, "The main logical component vertical coverage is below the configured provisional minimum.", main?.VerticalCoverage ?? 0d, minVertical);
        if (thresholds.MinimumLandingSiteCount is { } minLandingSites && landingSites < minLandingSites)
            Add(CaveValidationFailureCode.LandingSiteCountBelowMinimum, "Grounded landing-site count is below the configured minimum.", landingSites, minLandingSites);
        if (thresholds.MinimumPlayerClearLogicalTileCenters is { } minPlayerClear && playerClearLocations < minPlayerClear)
            Add(CaveValidationFailureCode.PlayerClearLocationCountBelowMinimum, "Player-clear logical tile-center count is below the configured minimum.", playerClearLocations, minPlayerClear);
        if (thresholds.MinimumEnemyValidNavigationCells is { } minEnemyCells && enemyValidCells < minEnemyCells)
            Add(CaveValidationFailureCode.EnemyValidCellCountBelowMinimum, "Enemy-clear navigation-cell count is below the configured minimum.", enemyValidCells, minEnemyCells);
        return failures;

        void Add(CaveValidationFailureCode code, string message, double observed, double required) =>
            failures.Add(new CaveValidationFailure(code, message, observed, required));
    }

    private static void ValidateInputs(CaveMap map, CaveGeometry geometry, CaveTopologyAnalysisOptions options)
    {
        if (geometry.BorderedWidth != map.Width + CaveGeometryBuilder.BorderSize * 2 ||
            geometry.BorderedHeight != map.Height + CaveGeometryBuilder.BorderSize * 2)
            throw new ArgumentException("Cave map and geometry dimensions do not correspond.", nameof(geometry));
        if (!float.IsFinite(options.LandingSampleSpacing) || options.LandingSampleSpacing <= 0f)
            throw new ArgumentOutOfRangeException(nameof(options.LandingSampleSpacing));
        if (!float.IsFinite(options.MinimumLandingSupportWidth) || options.MinimumLandingSupportWidth < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.MinimumLandingSupportWidth));
        if (options.HorizontalBandCount <= 0) throw new ArgumentOutOfRangeException(nameof(options.HorizontalBandCount));
        if (options.ElevationBandCount <= 0) throw new ArgumentOutOfRangeException(nameof(options.ElevationBandCount));
        if (!float.IsFinite(options.ApproximateBottleneckClearance) || options.ApproximateBottleneckClearance < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.ApproximateBottleneckClearance));
        if (!float.IsFinite(options.LandingNavigationSearchDistance) || options.LandingNavigationSearchDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(options.LandingNavigationSearchDistance));
        ValidateThresholds(options.Thresholds);
    }

    private static void ValidateThresholds(CaveQualityThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (thresholds.MinimumOpenSpacePercentage is < 0d or > 100d)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MinimumOpenSpacePercentage));
        if (thresholds.MaximumOpenSpacePercentage is < 0d or > 100d)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MaximumOpenSpacePercentage));
        if (thresholds.MinimumOpenSpacePercentage is { } minimumOpen &&
            thresholds.MaximumOpenSpacePercentage is { } maximumOpen && minimumOpen > maximumOpen)
            throw new ArgumentException("Minimum open-space percentage cannot exceed the maximum.", nameof(thresholds));
        if (thresholds.MinimumMainLogicalComponentShare is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MinimumMainLogicalComponentShare));
        if (thresholds.MinimumMainHorizontalCoverage is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MinimumMainHorizontalCoverage));
        if (thresholds.MinimumMainVerticalCoverage is < 0d or > 1d)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MinimumMainVerticalCoverage));
        if (thresholds.MinimumLandingSiteCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MinimumLandingSiteCount));
        if (thresholds.MinimumPlayerClearLogicalTileCenters is < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MinimumPlayerClearLogicalTileCenters));
        if (thresholds.MinimumEnemyValidNavigationCells is < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholds.MinimumEnemyValidNavigationCells));
    }

    private static Vector2 ToVector(CaveVertex point) => new(point.X, point.Y);

    private sealed class NavigationAccumulator(int id)
    {
        private int _cellCount;
        private int _enemyClearCount;
        private Vector2 _minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        private Vector2 _maximum = new(float.NegativeInfinity, float.NegativeInfinity);

        public void Add(Vector2 center, bool enemyClear)
        {
            _cellCount++;
            if (enemyClear) _enemyClearCount++;
            _minimum = Vector2.Min(_minimum, center);
            _maximum = Vector2.Max(_maximum, center);
        }

        public NavigationComponentReport ToReport() =>
            new(id, _cellCount, _enemyClearCount, _minimum, _maximum);
    }
}
