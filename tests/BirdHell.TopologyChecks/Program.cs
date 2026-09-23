using System.Globalization;
using BirdHell.Presentation;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Analysis;
using BirdHell.World.Geometry;

var analyzer = new CaveTopologyAnalyzer();

var empty = Analyze(CreateMap(9, 9));
Assert(empty.OpenTileCount == 0 && empty.LogicalComponents.Count == 0 && !empty.IsHardValid,
    "Empty map topology or hard validation is incorrect.");
Assert(empty.HardValidationFailures.Any(failure => failure.Code == CaveValidationFailureCode.NoOpenSpace) &&
       empty.HardValidationFailures.Any(failure => failure.Code == CaveValidationFailureCode.NoGroundedLandingSite),
    "Empty map failures do not explain missing open space and landing sites.");
Pass("empty map reports exact missing-topology failures");

var nearlyFull = Analyze(CreateBorderedOpenMap(9, 9));
Assert(nearlyFull.OpenTileCount == 49 && nearlyFull.LogicalComponents.Count == 1 &&
       nearlyFull.MainLogicalComponent?.TileCount == 49 && nearlyFull.MainLogicalComponentShare == 1d,
    "Nearly-full bordered map logical metrics are incorrect.");
Assert(nearlyFull.PlayerClearLogicalTileCenterCount > 0 && nearlyFull.LandingSites.Count > 0,
    "Nearly-full bordered map did not expose physical player clearance and floors.");
Pass("nearly-full map reports exact logical area and usable physical space");

var disconnected = Analyze(CreateDisconnectedMap());
Assert(disconnected.LogicalComponents.Count == 2 && disconnected.NavigationComponents.Count == 2 &&
       disconnected.MainLogicalComponentShare < 1d,
    "Disconnected map did not preserve distinct logical and navigation components.");
Assert(disconnected.IsHardValid,
    "Disconnected but geometrically usable map was silently rejected without a configured quality threshold.");
Pass("disconnected map preserves separate logical and navigation components");

var narrow = Analyze(CreateNarrowCorridorMap());
Assert(narrow.OpenTileCount > 0 && narrow.LogicalComponents.Count == 1 &&
       narrow.PlayerClearLogicalTileCenterCount == 0 && narrow.NavigationComponents.Count == 0,
    "Narrow corridor did not distinguish map-open space from capsule-clear and navigable space.");
Pass("map-open, player-clear, and navigation-reachable space remain distinct");

var bottleneck = Analyze(CreateBottleneckMap());
Assert(bottleneck.LogicalComponents.Count == 1 && bottleneck.NavigationComponents.Count == 1 &&
       bottleneck.ApproximateBottleneckCellCount > 0,
    "Connected authored bottleneck was not represented in approximate bottleneck metrics.");
Pass("connected narrow connector is measured as an approximate bottleneck");

var shelf = Analyze(CreateShelfMap());
Assert(shelf.FloorRuns.Count >= 2 && shelf.LandingSites.Count > 0 &&
       shelf.LandingSites.Select(site => site.ElevationBand).Distinct().Count() >= 2,
    "Wall-anchored shelf did not produce floors and landing sites at multiple elevations.");
Pass("wall-anchored shelf produces distributed landing elevations");

var multiElevation = Analyze(CreateMultiElevationMap());
Assert(multiElevation.LandingSites.Select(site => site.ElevationBand).Distinct().Count() >= 3,
    "Multi-elevation map did not populate at least three landing elevation bands.");
Assert(IsDeterministicallyOrdered(multiElevation.LandingSites),
    "Landing candidate ranking is not deterministic with stable coordinate tie-breaking.");
Pass("multi-elevation landing sites are distributed and deterministically ordered");

var thresholdGeometry = new CaveGeometryBuilder().Build(CreateBorderedOpenMap(9, 9));
var thresholdReport = analyzer.Analyze(CreateBorderedOpenMap(9, 9), thresholdGeometry, options:
    new CaveTopologyAnalysisOptions
    {
        Thresholds = new CaveQualityThresholds { MinimumLandingSiteCount = 10_000 },
    });
var thresholdFailure = thresholdReport.HardValidationFailures.Single(failure =>
    failure.Code == CaveValidationFailureCode.LandingSiteCountBelowMinimum);
Assert(thresholdFailure.Observed == thresholdReport.LandingSites.Count && thresholdFailure.Required == 10_000,
    "Configured threshold failure did not report its exact observed and required values.");
Pass("provisional thresholds are opt-in and failures expose exact observed values");

var repeatedMap = CreateShelfMap();
var repeatedGeometry = new CaveGeometryBuilder().Build(repeatedMap);
var firstReport = analyzer.Analyze(repeatedMap, repeatedGeometry);
var secondReport = analyzer.Analyze(repeatedMap, repeatedGeometry);
Assert(Fingerprint(firstReport) == Fingerprint(secondReport), "Repeated analysis produced different reports.");
Pass("analyzer output is repeatable for identical inputs");

const string isolationSeed = "m10-analyzer-rng-isolation";
var isolationMap = new CaveGenerator().Generate(isolationSeed);
var isolationGeometry = new CaveGeometryBuilder().Build(isolationMap);
var geometryBefore = GeometryFingerprint(isolationGeometry);
var environmentBefore = EnvironmentFingerprint(EnvironmentLayoutGenerator.Generate(
    isolationSeed, isolationGeometry.MinX, isolationGeometry.MaxX));
_ = analyzer.Analyze(isolationMap, isolationGeometry);
var regeneratedGeometry = new CaveGeometryBuilder().Build(new CaveGenerator().Generate(isolationSeed));
var environmentAfter = EnvironmentFingerprint(EnvironmentLayoutGenerator.Generate(
    isolationSeed, regeneratedGeometry.MinX, regeneratedGeometry.MaxX));
Assert(GeometryFingerprint(regeneratedGeometry) == geometryBefore && environmentAfter == environmentBefore,
    "Analyzer invocation perturbed gameplay generation or deterministic environment layout.");
Pass("analysis does not perturb gameplay or environment RNG");

var smokeSeeds = Enumerable.Range(0, 16).Select(index => $"m10-smoke-{index:D4}").ToArray();
var smokeReports = smokeSeeds.Select(seed => AnalyzeGenerated(seed)).ToArray();
Assert(smokeReports.All(result =>
    {
        var repeated = AnalyzeGenerated(result.Seed);
        return Fingerprint(result.Report) == Fingerprint(repeated.Report) &&
               result.Spawn.Selection == repeated.Spawn.Selection &&
               result.EnemySpawn?.Selection?.Placements.SequenceEqual(
                   repeated.EnemySpawn?.Selection?.Placements ?? []) == true;
    }),
    "Smoke corpus contains a non-repeatable analysis, player spawn, or enemy spawn result.");
Pass("16-seed smoke corpus is deterministic");

var evaluation = Enumerable.Range(0, 256)
    .Select(index => AnalyzeGenerated($"m10-quality-{index:D4}"))
    .ToArray();
PrintCorpusSummary("SMOKE", smokeReports);
PrintCorpusSummary("EVALUATION", evaluation);
var hardFailures = evaluation.Where(result => !result.Report.IsHardValid).ToArray();
Console.WriteLine($"CORPUS EVALUATION hard-failure-count={hardFailures.Length}");
foreach (var failure in hardFailures.Take(20))
    Console.WriteLine($"CORPUS FAILURE seed={failure.Seed} reasons={string.Join(',', failure.Report.HardValidationFailures.Select(item => item.Code))}");
var multipleNavigationComponents = evaluation.Where(result => result.Report.NavigationComponents.Count > 1).ToArray();
foreach (var outlier in multipleNavigationComponents)
    Console.WriteLine($"CORPUS OUTLIER seed={outlier.Seed} navigation-components={outlier.Report.NavigationComponents.Count} " +
                      $"component-sizes={string.Join(',', outlier.Report.NavigationComponents.Select(component => component.CellCount))}");
var provisionalFailures = evaluation.Where(result => FailsProvisionalThresholds(result.Report)).ToArray();
Console.WriteLine($"CORPUS EVALUATION provisional-threshold-failure-count={provisionalFailures.Length}");
foreach (var failure in provisionalFailures.Take(20))
    Console.WriteLine($"CORPUS PROVISIONAL-FAILURE seed={failure.Seed}");
var smokeSpawnFailures = smokeReports.Where(result => !result.Spawn.IsSuccess).ToArray();
var evaluationSpawnFailures = evaluation.Where(result => !result.Spawn.IsSuccess).ToArray();
Console.WriteLine($"CORPUS SMOKE spawn-pass={smokeReports.Length - smokeSpawnFailures.Length}/{smokeReports.Length}");
Console.WriteLine($"CORPUS EVALUATION spawn-pass={evaluation.Length - evaluationSpawnFailures.Length}/{evaluation.Length}");
foreach (var failure in smokeSpawnFailures.Concat(evaluationSpawnFailures).Take(20))
    Console.WriteLine($"CORPUS SPAWN-FAILURE seed={failure.Seed} reasons=" +
                      string.Join(',', failure.Spawn.Failure!.RejectionCounts.Where(pair => pair.Value > 0)
                          .Select(pair => $"{pair.Key}:{pair.Value}")));
var smokeEnemyFailures = smokeReports.Where(result => result.EnemySpawn is not { IsSuccess: true }).ToArray();
var evaluationEnemyFailures = evaluation.Where(result => result.EnemySpawn is not { IsSuccess: true }).ToArray();
Console.WriteLine($"CORPUS SMOKE enemy-spawn-pass={smokeReports.Length - smokeEnemyFailures.Length}/{smokeReports.Length}");
Console.WriteLine($"CORPUS EVALUATION enemy-spawn-pass={evaluation.Length - evaluationEnemyFailures.Length}/{evaluation.Length}");
foreach (var failure in smokeEnemyFailures.Concat(evaluationEnemyFailures).Take(20))
    Console.WriteLine($"CORPUS ENEMY-SPAWN-FAILURE seed={failure.Seed} " +
                      (failure.EnemySpawn?.Failure is { } detail
                          ? $"reason={detail.Code} candidates={detail.CandidateCount} selected={detail.SelectedCount}"
                          : "reason=player-spawn-failed"));
foreach (var example in smokeReports.Take(5))
{
    var spawn = example.Spawn.Selection!;
    var enemies = example.EnemySpawn!.Selection!;
    Console.WriteLine(FormattableString.Invariant(
        $"CORPUS SPAWN seed={example.Seed} position={spawn.Position.X:F4},{spawn.Position.Y:F4} navigation-component={spawn.NavigationComponentId} component-cells={spawn.NavigationComponentCellCount} local-cells={spawn.LocalMovementCellCount} enemy-candidates={enemies.CandidateCount} enemy-min-spacing={enemies.MinimumPairwiseSeparation:F4} enemy-min-player-distance={enemies.MinimumPlayerDistance:F4}"));
}
Pass("256-seed unchanged-parameter evaluation corpus completed");

CaveTopologyReport Analyze(CaveMap map)
{
    var geometry = new CaveGeometryBuilder().Build(map);
    return analyzer.Analyze(map, geometry);
}

(string Seed, CaveTopologyReport Report, PlayerSpawnResult Spawn, EnemySpawnResult? EnemySpawn)
    AnalyzeGenerated(string seed)
{
    var map = new CaveGenerator().Generate(seed);
    var geometry = new CaveGeometryBuilder().Build(map);
    var occupancy = new TerrainOccupancy(geometry);
    var navigation = new NavigationGrid(occupancy);
    var collision = new TerrainCollisionSystem(occupancy);
    var report = analyzer.Analyze(map, geometry, occupancy, navigation);
    var spawn = PlayerSpawnSystem.Select(
        report, geometry, occupancy, collision, navigation, BirdHell.Entities.PlayerCollider.Original);
    if (!spawn.IsSuccess) return (seed, report, spawn, null);
    var player = new BirdHell.Entities.Player(spawn.Selection!.Position);
    collision.RefreshContacts(player);
    var enemySpawn = EnemySpawnSystem.Select(occupancy, navigation, player, spawn.Selection, seed);
    return (seed, report, spawn, enemySpawn);
}

static CaveMap CreateMap(int width, int height)
{
    var tiles = new CaveTile[width, height];
    for (var x = 0; x < width; x++)
    for (var y = 0; y < height; y++)
        tiles[x, y] = CaveTile.Wall;
    return new CaveMap(tiles);
}

static CaveMap CreateBorderedOpenMap(int width, int height)
{
    var tiles = WallTiles(width, height);
    OpenRectangle(tiles, 1, width - 2, 1, height - 2);
    return new CaveMap(tiles);
}

static CaveMap CreateDisconnectedMap()
{
    var tiles = WallTiles(17, 11);
    OpenRectangle(tiles, 1, 6, 1, 9);
    OpenRectangle(tiles, 10, 15, 1, 9);
    return new CaveMap(tiles);
}

static CaveMap CreateNarrowCorridorMap()
{
    var tiles = WallTiles(17, 9);
    OpenRectangle(tiles, 1, 15, 4, 4);
    return new CaveMap(tiles);
}

static CaveMap CreateBottleneckMap()
{
    var tiles = WallTiles(21, 11);
    OpenRectangle(tiles, 1, 7, 1, 9);
    OpenRectangle(tiles, 13, 19, 1, 9);
    OpenRectangle(tiles, 8, 12, 4, 6);
    return new CaveMap(tiles);
}

static CaveMap CreateShelfMap()
{
    var tiles = WallTiles(17, 13);
    OpenRectangle(tiles, 1, 15, 1, 11);
    for (var x = 1; x <= 8; x++) tiles[x, 6] = CaveTile.Wall;
    return new CaveMap(tiles);
}

static CaveMap CreateMultiElevationMap()
{
    var tiles = WallTiles(21, 17);
    OpenRectangle(tiles, 1, 19, 1, 15);
    for (var x = 1; x <= 7; x++) tiles[x, 5] = CaveTile.Wall;
    for (var x = 13; x <= 19; x++) tiles[x, 9] = CaveTile.Wall;
    for (var x = 1; x <= 6; x++) tiles[x, 13] = CaveTile.Wall;
    return new CaveMap(tiles);
}

static CaveTile[,] WallTiles(int width, int height)
{
    var tiles = new CaveTile[width, height];
    for (var x = 0; x < width; x++)
    for (var y = 0; y < height; y++)
        tiles[x, y] = CaveTile.Wall;
    return tiles;
}

static void OpenRectangle(CaveTile[,] tiles, int minX, int maxX, int minY, int maxY)
{
    for (var x = minX; x <= maxX; x++)
    for (var y = minY; y <= maxY; y++)
        tiles[x, y] = CaveTile.Open;
}

static bool IsDeterministicallyOrdered(IReadOnlyList<LandingSiteReport> sites)
{
    for (var index = 0; index < sites.Count; index++)
    {
        if (sites[index].Rank != index) return false;
        if (index == 0) continue;
        var previous = sites[index - 1];
        var current = sites[index];
        if (previous.SupportWidth < current.SupportWidth) return false;
        if (previous.SupportWidth == current.SupportWidth && previous.Headroom < current.Headroom) return false;
        if (previous.SupportWidth == current.SupportWidth && previous.Headroom == current.Headroom &&
            previous.SurfacePoint.X > current.SurfacePoint.X) return false;
    }
    return true;
}

static string Fingerprint(CaveTopologyReport report) => string.Join('|',
    report.OpenTileCount,
    report.OpenSpacePercentage.ToString("R", CultureInfo.InvariantCulture),
    report.MainLogicalComponentShare.ToString("R", CultureInfo.InvariantCulture),
    string.Join(';', report.LogicalComponents),
    string.Join(';', report.FloorRuns),
    string.Join(';', report.LandingSites),
    report.PlayerClearLogicalTileCenterCount,
    report.PlayerClearNavigationCellCount,
    string.Join(';', report.NavigationComponents),
    report.EnemyValidNavigationCellCount,
    report.ApproximateBottleneckCellCount,
    string.Join(',', report.HardValidationFailures.Select(failure => failure.Code)));

static string GeometryFingerprint(CaveGeometry geometry) =>
    string.Join('|', geometry.Vertices.Select(vertex => $"{vertex.X:R},{vertex.Y:R}")) + ":" +
    string.Join('|', geometry.Triangles.Select(triangle => $"{triangle.A},{triangle.B},{triangle.C}"));

static string EnvironmentFingerprint(EnvironmentLayout layout) => string.Join('|',
    layout.Layers.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value).Select(instance =>
        $"{instance.TextureId}:{instance.Position.X:R},{instance.Position.Y:R}:{instance.Scale:R}:{instance.FlipHorizontally}"));

static void PrintCorpusSummary(
    string name,
    IReadOnlyList<(string Seed, CaveTopologyReport Report, PlayerSpawnResult Spawn, EnemySpawnResult? EnemySpawn)> corpus)
{
    Print("open-percent", corpus.Select(item => item.Report.OpenSpacePercentage));
    Print("main-share", corpus.Select(item => item.Report.MainLogicalComponentShare * 100d));
    Print("horizontal-coverage", corpus.Select(item => (item.Report.MainLogicalComponent?.HorizontalCoverage ?? 0d) * 100d));
    Print("vertical-coverage", corpus.Select(item => (item.Report.MainLogicalComponent?.VerticalCoverage ?? 0d) * 100d));
    Print("floor-horizontal-width", corpus.SelectMany(item => item.Report.FloorRuns.Select(run => (double)run.HorizontalSpan)));
    Print("floor-surface-length", corpus.SelectMany(item => item.Report.FloorRuns.Select(run => (double)run.SurfaceLength)));
    Print("landing-count", corpus.Select(item => (double)item.Report.LandingSites.Count));
    Print("landing-elevation-bands", corpus.Select(item => (double)item.Report.LandingSitesByElevationBand.Count(count => count > 0)));
    Print("landing-elevation-span", corpus.Select(item => item.Report.LandingSites.Count == 0
        ? 0d
        : item.Report.LandingSites.Max(site => site.SurfacePoint.Y) - item.Report.LandingSites.Min(site => site.SurfacePoint.Y)));
    Print("player-clear-logical", corpus.Select(item => (double)item.Report.PlayerClearLogicalTileCenterCount));
    Print("player-clear-navigation", corpus.Select(item => (double)item.Report.PlayerClearNavigationCellCount));
    Print("enemy-valid-navigation", corpus.Select(item => (double)item.Report.EnemyValidNavigationCellCount));
    Print("navigation-components", corpus.Select(item => (double)item.Report.NavigationComponents.Count));
    Print("approximate-bottlenecks", corpus.Select(item => (double)item.Report.ApproximateBottleneckCellCount));
    Print("spawn-component-cells", corpus.Where(item => item.Spawn.IsSuccess)
        .Select(item => (double)item.Spawn.Selection!.NavigationComponentCellCount));
    Print("spawn-local-movement-cells", corpus.Where(item => item.Spawn.IsSuccess)
        .Select(item => (double)item.Spawn.Selection!.LocalMovementCellCount));
    Print("enemy-spawn-candidates", corpus.Where(item => item.EnemySpawn is { IsSuccess: true })
        .Select(item => (double)item.EnemySpawn!.Selection!.CandidateCount));
    Print("enemy-minimum-spacing", corpus.Where(item => item.EnemySpawn is { IsSuccess: true })
        .Select(item => (double)item.EnemySpawn!.Selection!.MinimumPairwiseSeparation));
    Print("enemy-minimum-player-distance", corpus.Where(item => item.EnemySpawn is { IsSuccess: true })
        .Select(item => (double)item.EnemySpawn!.Selection!.MinimumPlayerDistance));
    Print("enemy-horizontal-span", corpus.Where(item => item.EnemySpawn is { IsSuccess: true })
        .Select(item => (double)item.EnemySpawn!.Selection!.HorizontalSpan));
    Print("enemy-vertical-span", corpus.Where(item => item.EnemySpawn is { IsSuccess: true })
        .Select(item => (double)item.EnemySpawn!.Selection!.VerticalSpan));
    Print("enemy-horizontal-bands", corpus.Where(item => item.EnemySpawn is { IsSuccess: true })
        .Select(item => (double)item.EnemySpawn!.Selection!.OccupiedHorizontalBands));
    Print("enemy-vertical-bands", corpus.Where(item => item.EnemySpawn is { IsSuccess: true })
        .Select(item => (double)item.EnemySpawn!.Selection!.OccupiedVerticalBands));
    return;

    void Print(string metric, IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            Console.WriteLine($"CORPUS {name} metric={metric} count=0");
            return;
        }
        double Percentile(double fraction) => ordered[(int)Math.Round((ordered.Length - 1) * fraction)];
        Console.WriteLine(FormattableString.Invariant(
            $"CORPUS {name} metric={metric} count={ordered.Length} min={ordered[0]:F4} p10={Percentile(0.10):F4} p50={Percentile(0.50):F4} p90={Percentile(0.90):F4} max={ordered[^1]:F4} mean={ordered.Average():F4}"));
    }
}

static bool FailsProvisionalThresholds(CaveTopologyReport report) =>
    report.OpenSpacePercentage < 75d || report.OpenSpacePercentage > 87d ||
    report.MainLogicalComponentShare < 0.99d ||
    (report.MainLogicalComponent?.HorizontalCoverage ?? 0d) < 0.95d ||
    (report.MainLogicalComponent?.VerticalCoverage ?? 0d) < 0.90d ||
    report.LandingSites.Count < 350 ||
    report.PlayerClearLogicalTileCenterCount < 2500 ||
    report.EnemyValidNavigationCellCount < 2500;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Pass(string message) => Console.WriteLine($"PASS: {message}");
