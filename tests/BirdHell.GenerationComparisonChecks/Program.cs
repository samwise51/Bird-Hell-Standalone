using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Analysis;
using BirdHell.World.Geometry;

var profiles = new[]
{
    new ProfileSpec("baseline", "Baseline", CaveGenerationProfiles.Baseline, false),
    new ProfileSpec("conservative-irregular", "Conservative irregular", CaveGenerationProfiles.ConservativeIrregular, false),
    new ProfileSpec("dense", "Dense", CaveGenerationProfiles.Dense, true),
    new ProfileSpec("complex", "Complex", CaveGenerationProfiles.Complex, true),
    new ProfileSpec("extreme", "Extreme", CaveGenerationProfiles.Extreme, true),
};
var baselineProfile = profiles[0];
var conservativeProfile = profiles[1];

AssertOptions(baselineProfile.Options, 128, 30, 35, 5, 50, 50, 5);
AssertOptions(conservativeProfile.Options, 128, 30, 38, 4, 40, 50, 4);
AssertOptions(profiles[2].Options, 128, 30, 42, 4, 40, 50, 4);
AssertOptions(profiles[3].Options, 128, 30, 45, 3, 35, 50, 3);
AssertOptions(profiles[4].Options, 128, 30, 48, 3, 35, 50, 3);
Assert(MapFingerprint(new CaveGenerator().Generate("m10e-default-proof")) ==
       MapFingerprint(new CaveGenerator(baselineProfile.Options).Generate("m10e-default-proof")),
    "Named baseline differs from the unchanged gameplay-default generator.");
ValidateInvalidOptions();
ValidateDeterminismAndRngIsolation(profiles);
Pass("baseline, conservative, candidate parameters, unchanged default, and deterministic RNG isolation are exact");

var seeds = Enumerable.Range(0, 256).Select(index => $"m10-quality-{index:D4}").ToArray();
var outcomes = profiles.ToDictionary(profile => profile.Name, _ => new List<EvaluationOutcome>(seeds.Length));
for (var index = 0; index < seeds.Length; index++)
{
    // Rotate evaluation order to avoid consistently assigning warm-cache effects to one profile.
    for (var offset = 0; offset < profiles.Length; offset++)
    {
        var profile = profiles[(index + offset) % profiles.Length];
        outcomes[profile.Name].Add(Evaluate(profile.Name, profile.Options, seeds[index]));
    }
}
foreach (var profile in profiles)
{
    outcomes[profile.Name].Sort((left, right) => string.CompareOrdinal(left.Seed, right.Seed));
    Assert(outcomes[profile.Name].Count == seeds.Length &&
           outcomes[profile.Name].Select(item => item.Seed).SequenceEqual(seeds),
        $"Profile {profile.Name} did not evaluate every fixed-corpus seed exactly once.");
    PrintProfileSummary(profile.Name, outcomes[profile.Name]);
    PrintFailures(profile.Name, outcomes[profile.Name]);
    Console.WriteLine($"COMPARE provisional-threshold-failures profile={profile.Name} count={outcomes[profile.Name].Count(FailsBaselineSpecificThresholds)}");
}

var comparisons = new List<ProfileComparison>();
foreach (var candidate in profiles.Where(profile => profile.IsCandidate))
{
    foreach (var reference in new[] { baselineProfile, conservativeProfile })
    {
        var pairs = Pair(outcomes[reference.Name], outcomes[candidate.Name]);
        comparisons.Add(new ProfileComparison(reference, candidate, pairs));
        PrintDeltaSummary(reference.Name, candidate.Name, pairs);
    }
}

var outputDirectory = Path.Combine(FindRepositoryRoot(), "docs", "milestone-10g");
Directory.CreateDirectory(outputDirectory);
var imagePaths = new List<string>();
foreach (var candidate in profiles.Where(profile => profile.IsCandidate))
{
    var baselinePairs = Pair(outcomes[baselineProfile.Name], outcomes[candidate.Name]);
    var conservativeBySeed = outcomes[conservativeProfile.Name].ToDictionary(item => item.Seed);
    var representative = SelectRepresentativeSeeds(baselinePairs
        .Where(pair => conservativeBySeed[pair.Reference.Seed].Success)
        .ToArray());
    foreach (var seed in representative)
    {
        var snapshots = new[]
        {
            BuildSnapshot(baselineProfile.Name, baselineProfile.Options, seed),
            BuildSnapshot(conservativeProfile.Name, conservativeProfile.Options, seed),
            BuildSnapshot(candidate.Name, candidate.Options, seed),
        };
        var path = Path.Combine(outputDirectory,
            $"{seed}-baseline-vs-conservative-vs-{candidate.Name}.svg");
        File.WriteAllText(path, RenderComparisonSvg(seed, snapshots));
        imagePaths.Add(path);
        Console.WriteLine($"COMPARE IMAGE {path}");
    }
}

var csvPath = Path.Combine(outputDirectory, "comparison-results.csv");
File.WriteAllText(csvPath, BuildCsv(profiles.SelectMany(profile => outcomes[profile.Name])));
var reportPath = Path.Combine(outputDirectory, "comparison-summary.md");
File.WriteAllText(reportPath, BuildMarkdownReport(profiles, outcomes, comparisons, imagePaths));
Console.WriteLine($"COMPARE DATA {csvPath}");
Console.WriteLine($"COMPARE REPORT {reportPath}");
Pass("five-profile, 256-seed generation comparison and representative diagnostic exports completed");

static EvaluationOutcome Evaluate(string profile, CaveGenerationOptions options, string seed)
{
    var totalTimer = Stopwatch.StartNew();
    var generationTimer = Stopwatch.StartNew();
    var currentStage = "generation";
    CaveMap map;
    try
    {
        map = new CaveGenerator(options).Generate(seed);
    }
    catch (Exception exception)
    {
        generationTimer.Stop();
        totalTimer.Stop();
        return EvaluationOutcome.Failed(profile, seed, "generation", exception.Message,
            generationTimer.Elapsed.TotalMilliseconds, totalTimer.Elapsed.TotalMilliseconds);
    }
    generationTimer.Stop();

    try
    {
        currentStage = "geometry";
        var geometry = new CaveGeometryBuilder().Build(map);
        var occupancy = new TerrainOccupancy(geometry);
        var navigation = new NavigationGrid(occupancy);
        var collision = new TerrainCollisionSystem(occupancy);
        currentStage = "topology";
        var topology = new CaveTopologyAnalyzer().Analyze(map, geometry, occupancy, navigation);
        if (!topology.IsHardValid)
        {
            totalTimer.Stop();
            return FromTopologyFailure("topology", string.Join(',', topology.HardValidationFailures.Select(item => item.Code)));
        }

        currentStage = "player-spawn";
        var playerSpawn = PlayerSpawnSystem.Select(
            topology, geometry, occupancy, collision, navigation, PlayerCollider.Original);
        if (!playerSpawn.IsSuccess)
        {
            totalTimer.Stop();
            return FromTopologyFailure("player-spawn", playerSpawn.Failure!.Message);
        }

        var player = new Player(playerSpawn.Selection!.Position);
        collision.RefreshContacts(player);
        currentStage = "enemy-spawn";
        var enemySpawn = EnemySpawnSystem.Select(occupancy, navigation, player, playerSpawn.Selection, seed);
        if (!enemySpawn.IsSuccess)
        {
            totalTimer.Stop();
            return FromTopologyFailure("enemy-spawn", enemySpawn.Failure!.Message);
        }
        if (enemySpawn.Selection!.Placements.Count != EnemySpawnSystem.OriginalEnemyCount)
        {
            totalTimer.Stop();
            return FromTopologyFailure("enemy-spawn",
                $"Expected {EnemySpawnSystem.OriginalEnemyCount} enemies but selected {enemySpawn.Selection.Placements.Count}.");
        }

        totalTimer.Stop();
        var floorWidths = topology.FloorRuns.Select(run => (double)run.HorizontalSpan).Order().ToArray();
        var elevationSpan = topology.LandingSites.Count == 0
            ? 0d
            : topology.LandingSites.Max(site => site.SurfacePoint.Y) -
              topology.LandingSites.Min(site => site.SurfacePoint.Y);
        var totalNavigationCells = topology.NavigationComponents.Sum(component => component.CellCount);
        var metrics = new EvaluationMetrics(
            topology.OpenSpacePercentage,
            topology.OpenTileCount,
            topology.LogicalComponents.Count,
            topology.MainLogicalComponentShare,
            topology.NavigationComponents.Count,
            totalNavigationCells,
            topology.PlayerClearLogicalTileCenterCount,
            playerSpawn.Selection.NavigationComponentCellCount,
            topology.FloorRuns.Count,
            floorWidths.Length == 0 ? 0d : Percentile(floorWidths, 0.5),
            topology.FloorRuns.Count(run => run.HorizontalSpan is >= 1f and <= 12f),
            topology.FloorSurfaces.Count(surface => surface.IsWalkable && surface.SlopeDegrees > 0.1f),
            topology.LandingSites.Count,
            topology.LandingSites.Select(site => site.FloorRunId).Distinct().Count(),
            topology.LandingSitesByElevationBand.Count(count => count > 0),
            elevationSpan,
            topology.ApproximateBottleneckCellCount,
            topology.PlayerClearNavigationCellCount,
            topology.EnemyValidNavigationCellCount,
            enemySpawn.Selection!.CandidateCount,
            enemySpawn.Selection.MinimumPairwiseSeparation,
            enemySpawn.Selection.MinimumPlayerDistance,
            enemySpawn.Selection.HorizontalSpan,
            enemySpawn.Selection.VerticalSpan);
        return EvaluationOutcome.Succeeded(profile, seed, generationTimer.Elapsed.TotalMilliseconds,
            totalTimer.Elapsed.TotalMilliseconds, metrics);

        EvaluationOutcome FromTopologyFailure(string stage, string reason)
        {
            var floorWidths = topology.FloorRuns.Select(run => (double)run.HorizontalSpan).Order().ToArray();
            var elevationSpan = topology.LandingSites.Count == 0
                ? 0d
                : topology.LandingSites.Max(site => site.SurfacePoint.Y) -
                  topology.LandingSites.Min(site => site.SurfacePoint.Y);
            var metrics = new EvaluationMetrics(
                topology.OpenSpacePercentage,
                topology.OpenTileCount,
                topology.LogicalComponents.Count,
                topology.MainLogicalComponentShare,
                topology.NavigationComponents.Count,
                topology.NavigationComponents.Sum(component => component.CellCount),
                topology.PlayerClearLogicalTileCenterCount,
                0,
                topology.FloorRuns.Count,
                floorWidths.Length == 0 ? 0d : Percentile(floorWidths, 0.5),
                topology.FloorRuns.Count(run => run.HorizontalSpan is >= 1f and <= 12f),
                topology.FloorSurfaces.Count(surface => surface.IsWalkable && surface.SlopeDegrees > 0.1f),
                topology.LandingSites.Count,
                topology.LandingSites.Select(site => site.FloorRunId).Distinct().Count(),
                topology.LandingSitesByElevationBand.Count(count => count > 0),
                elevationSpan,
                topology.ApproximateBottleneckCellCount,
                topology.PlayerClearNavigationCellCount,
                topology.EnemyValidNavigationCellCount,
                0, 0, 0, 0, 0);
            return EvaluationOutcome.Failed(profile, seed, stage, reason,
                generationTimer.Elapsed.TotalMilliseconds, totalTimer.Elapsed.TotalMilliseconds, metrics);
        }
    }
    catch (Exception exception)
    {
        totalTimer.Stop();
        return EvaluationOutcome.Failed(profile, seed, currentStage, exception.Message,
            generationTimer.Elapsed.TotalMilliseconds, totalTimer.Elapsed.TotalMilliseconds);
    }
}

static PipelineSnapshot BuildSnapshot(string profile, CaveGenerationOptions options, string seed)
{
    var map = new CaveGenerator(options).Generate(seed);
    var geometry = new CaveGeometryBuilder().Build(map);
    var occupancy = new TerrainOccupancy(geometry);
    var navigation = new NavigationGrid(occupancy);
    var collision = new TerrainCollisionSystem(occupancy);
    var topology = new CaveTopologyAnalyzer().Analyze(map, geometry, occupancy, navigation);
    var playerSpawn = PlayerSpawnSystem.Select(
        topology, geometry, occupancy, collision, navigation, PlayerCollider.Original);
    if (!playerSpawn.IsSuccess) throw new InvalidOperationException($"Representative {profile}/{seed} has no player spawn.");
    var player = new Player(playerSpawn.Selection!.Position);
    collision.RefreshContacts(player);
    var enemies = EnemySpawnSystem.Select(occupancy, navigation, player, playerSpawn.Selection, seed);
    if (!enemies.IsSuccess) throw new InvalidOperationException($"Representative {profile}/{seed} has no enemy spawn.");
    if (enemies.Selection!.Placements.Count != EnemySpawnSystem.OriginalEnemyCount)
        throw new InvalidOperationException(
            $"Representative {profile}/{seed} selected {enemies.Selection.Placements.Count} enemies instead of {EnemySpawnSystem.OriginalEnemyCount}.");
    return new PipelineSnapshot(profile, map, geometry, topology, playerSpawn.Selection, enemies.Selection!);
}

static void PrintProfileSummary(string name, IReadOnlyList<EvaluationOutcome> outcomes)
{
    Console.WriteLine($"COMPARE PROFILE {name} success={outcomes.Count(item => item.Success)}/{outcomes.Count}");
    Print("generation-ms", outcomes.Select(item => item.GenerationMilliseconds));
    Print("pipeline-ms", outcomes.Select(item => item.PipelineMilliseconds));
    PrintMetric("open-percent", metrics => metrics.OpenSpacePercentage);
    PrintMetric("open-tiles", metrics => metrics.OpenTileCount);
    PrintMetric("logical-components", metrics => metrics.LogicalComponentCount);
    PrintMetric("main-logical-share-percent", metrics => metrics.MainLogicalComponentShare * 100d);
    PrintMetric("navigation-components", metrics => metrics.NavigationComponentCount);
    PrintMetric("navigation-cells", metrics => metrics.NavigationCellCount);
    PrintMetric("player-clear-logical-centers", metrics => metrics.PlayerClearLogicalTileCenterCount);
    PrintMetric("player-component-cells", metrics => metrics.PlayerComponentCellCount);
    PrintMetric("floor-run-count", metrics => metrics.FloorRunCount);
    PrintMetric("median-floor-width", metrics => metrics.MedianFloorWidth);
    PrintMetric("platform-scale-floor-runs", metrics => metrics.PlatformScaleFloorRunCount);
    PrintMetric("sloped-floor-surfaces", metrics => metrics.SlopedFloorSurfaceCount);
    PrintMetric("landing-sites", metrics => metrics.LandingSiteCount);
    PrintMetric("landing-floor-runs", metrics => metrics.LandingFloorRunCount);
    PrintMetric("landing-elevation-bands", metrics => metrics.LandingElevationBandCount);
    PrintMetric("landing-elevation-span", metrics => metrics.LandingElevationSpan);
    PrintMetric("bottlenecks", metrics => metrics.BottleneckCount);
    PrintMetric("player-clear-navigation", metrics => metrics.PlayerClearNavigationCellCount);
    PrintMetric("enemy-valid-navigation", metrics => metrics.EnemyValidNavigationCellCount);
    PrintMetric("enemy-candidates", metrics => metrics.EnemyCandidateCount);
    PrintMetric("enemy-minimum-spacing", metrics => metrics.EnemyMinimumSpacing);
    PrintMetric("enemy-minimum-player-distance", metrics => metrics.EnemyMinimumPlayerDistance);
    return;

    void PrintMetric(string metric, Func<EvaluationMetrics, double> selector) =>
        Print(metric, outcomes.Where(item => item.Metrics is not null).Select(item => selector(item.Metrics!)));

    void Print(string metric, IEnumerable<double> values)
    {
        var distribution = Distribution.From(values);
        Console.WriteLine(FormattableString.Invariant(
            $"COMPARE {name} metric={metric} count={distribution.Count} min={distribution.Minimum:F4} p50={distribution.Median:F4} max={distribution.Maximum:F4} mean={distribution.Mean:F4}"));
    }
}

static void PrintFailures(string profile, IReadOnlyList<EvaluationOutcome> outcomes)
{
    var failures = outcomes.Where(item => !item.Success).ToArray();
    Console.WriteLine($"COMPARE FAILURES profile={profile} count={failures.Length}");
    foreach (var failure in failures)
        Console.WriteLine($"COMPARE FAILURE profile={profile} seed={failure.Seed} stage={failure.FailureStage} reason={failure.FailureReason}");
}

static void PrintDeltaSummary(string referenceName, string candidateName, IReadOnlyList<PairedOutcome> pairs)
{
    var successful = pairs.Where(pair => pair.Reference.Metrics is not null && pair.Candidate.Metrics is not null).ToArray();
    PrintMetricsDelta("open-percent", metrics => metrics.OpenSpacePercentage);
    PrintMetricsDelta("floor-run-count", metrics => metrics.FloorRunCount);
    PrintMetricsDelta("median-floor-width", metrics => metrics.MedianFloorWidth);
    PrintMetricsDelta("platform-scale-floor-runs", metrics => metrics.PlatformScaleFloorRunCount);
    PrintMetricsDelta("sloped-floor-surfaces", metrics => metrics.SlopedFloorSurfaceCount);
    PrintMetricsDelta("landing-sites", metrics => metrics.LandingSiteCount);
    PrintMetricsDelta("landing-floor-runs", metrics => metrics.LandingFloorRunCount);
    PrintMetricsDelta("landing-elevation-bands", metrics => metrics.LandingElevationBandCount);
    PrintMetricsDelta("landing-elevation-span", metrics => metrics.LandingElevationSpan);
    PrintMetricsDelta("bottlenecks", metrics => metrics.BottleneckCount);
    PrintMetricsDelta("navigation-cells", metrics => metrics.NavigationCellCount);
    PrintOutcomeDelta("generation-ms", outcome => outcome.GenerationMilliseconds);
    PrintOutcomeDelta("pipeline-ms", outcome => outcome.PipelineMilliseconds);
    return;

    void PrintMetricsDelta(string name, Func<EvaluationMetrics, double> selector)
    {
        var deltas = successful.Select(pair => selector(pair.Candidate.Metrics!) - selector(pair.Reference.Metrics!));
        var distribution = Distribution.From(deltas);
        Console.WriteLine(FormattableString.Invariant(
            $"COMPARE DELTA candidate={candidateName} reference={referenceName} metric={name} min={distribution.Minimum:F4} p50={distribution.Median:F4} max={distribution.Maximum:F4} mean={distribution.Mean:F4}"));
    }

    void PrintOutcomeDelta(string name, Func<EvaluationOutcome, double> selector)
    {
        var deltas = successful.Select(pair => selector(pair.Candidate) - selector(pair.Reference));
        var distribution = Distribution.From(deltas);
        Console.WriteLine(FormattableString.Invariant(
            $"COMPARE DELTA candidate={candidateName} reference={referenceName} metric={name} min={distribution.Minimum:F4} p50={distribution.Median:F4} max={distribution.Maximum:F4} mean={distribution.Mean:F4}"));
    }
}

static IReadOnlyList<string> SelectRepresentativeSeeds(IReadOnlyList<PairedOutcome> pairs)
{
    var successful = pairs.Where(pair => pair.Reference.Success && pair.Candidate.Success)
        .OrderBy(OpportunityDelta)
        .ToArray();
    if (successful.Length == 0) return [];
    return new[]
        {
            successful[^1].Reference.Seed,
            successful[successful.Length / 2].Reference.Seed,
            successful[0].Reference.Seed,
        }
        .Distinct()
        .ToArray();

    static double OpportunityDelta(PairedOutcome pair) =>
        OpportunityScore(pair.Candidate.Metrics!) - OpportunityScore(pair.Reference.Metrics!);
}

static double OpportunityScore(EvaluationMetrics metrics) =>
    metrics.PlatformScaleFloorRunCount * 2d +
    metrics.LandingFloorRunCount * 2d +
    metrics.LandingElevationBandCount * 12d +
    metrics.LandingElevationSpan * 2d +
    metrics.SlopedFloorSurfaceCount * 0.5d +
    metrics.BottleneckCount * 0.1d;

static string RenderComparisonSvg(string seed, IReadOnlyList<PipelineSnapshot> snapshots)
{
    if (snapshots.Count == 0) throw new ArgumentException("At least one snapshot is required.", nameof(snapshots));
    const int scale = 6;
    const int panelGap = 28;
    const int margin = 18;
    const int titleHeight = 58;
    var panelWidth = snapshots[0].Map.Width * scale;
    var panelHeight = snapshots[0].Map.Height * scale;
    Assert(snapshots.All(snapshot => snapshot.Map.Width == snapshots[0].Map.Width &&
                                     snapshot.Map.Height == snapshots[0].Map.Height),
        "SVG snapshots must use identical map dimensions.");
    var width = margin * 2 + panelWidth * snapshots.Count + panelGap * (snapshots.Count - 1);
    var height = titleHeight + panelHeight + 48;
    var builder = new StringBuilder();
    builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
    builder.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#111522\"/>");
    builder.AppendLine($"<text x=\"{margin}\" y=\"24\" fill=\"#f4f1ff\" font-family=\"monospace\" font-size=\"16\">{seed}: {string.Join(" vs ", snapshots.Select(snapshot => snapshot.Profile))}</text>");
    for (var index = 0; index < snapshots.Count; index++)
        RenderPanel(snapshots[index], margin + index * (panelWidth + panelGap), titleHeight);
    builder.AppendLine("</svg>");
    return builder.ToString();

    void RenderPanel(PipelineSnapshot snapshot, int offsetX, int offsetY)
    {
        builder.AppendLine($"<rect x=\"{offsetX}\" y=\"{offsetY}\" width=\"{panelWidth}\" height=\"{panelHeight}\" fill=\"#d9d1ea\"/>");
        for (var y = 0; y < snapshot.Map.Height; y++)
        {
            var x = 0;
            while (x < snapshot.Map.Width)
            {
                if (snapshot.Map[x, y] != CaveTile.Wall)
                {
                    x++;
                    continue;
                }
                var start = x;
                while (x < snapshot.Map.Width && snapshot.Map[x, y] == CaveTile.Wall) x++;
                builder.AppendLine($"<rect x=\"{offsetX + start * scale}\" y=\"{offsetY + (snapshot.Map.Height - 1 - y) * scale}\" width=\"{(x - start) * scale}\" height=\"{scale}\" fill=\"#27233a\"/>");
            }
        }

        foreach (var landing in snapshot.Topology.LandingSites.Where((_, index) => index % 8 == 0))
        {
            var point = ToPanel(landing.SurfacePoint);
            builder.AppendLine(FormattableString.Invariant($"<circle cx=\"{point.X:F2}\" cy=\"{point.Y:F2}\" r=\"1.1\" fill=\"#42d9c8\" opacity=\"0.8\"/>"));
        }
        foreach (var enemy in snapshot.Enemies.Placements)
        {
            var point = ToPanel(enemy.ColliderCenter);
            builder.AppendLine(FormattableString.Invariant($"<circle cx=\"{point.X:F2}\" cy=\"{point.Y:F2}\" r=\"3\" fill=\"#ff9f43\" stroke=\"#532b00\" stroke-width=\"0.6\"/>"));
        }
        var player = ToPanel(snapshot.Player.ColliderCenter);
        builder.AppendLine(FormattableString.Invariant($"<circle cx=\"{player.X:F2}\" cy=\"{player.Y:F2}\" r=\"4\" fill=\"#ff4d88\" stroke=\"white\" stroke-width=\"1\"/>"));
        builder.AppendLine($"<text x=\"{offsetX}\" y=\"{offsetY - 8}\" fill=\"#f4f1ff\" font-family=\"monospace\" font-size=\"13\">{snapshot.Profile}</text>");
        builder.AppendLine($"<text x=\"{offsetX}\" y=\"{offsetY + panelHeight + 18}\" fill=\"#bfc5db\" font-family=\"monospace\" font-size=\"11\">open {snapshot.Topology.OpenSpacePercentage:F1}% | floors {snapshot.Topology.FloorRuns.Count} | landings {snapshot.Topology.LandingSites.Count} | elev bands {snapshot.Topology.LandingSitesByElevationBand.Count(count => count > 0)}</text>");

        Vector2 ToPanel(Vector2 world) => new(
            offsetX + (world.X - snapshot.Geometry.MinX) / (snapshot.Geometry.MaxX - snapshot.Geometry.MinX) * panelWidth,
            offsetY + (snapshot.Geometry.MaxY - world.Y) / (snapshot.Geometry.MaxY - snapshot.Geometry.MinY) * panelHeight);
    }
}

static string BuildCsv(IEnumerable<EvaluationOutcome> outcomes)
{
    var builder = new StringBuilder();
    builder.AppendLine("profile,seed,pipeline_success,generation_success,topology_success,player_spawn_success,enemy_15_spawn_success,enemy_spawn_count,failure_stage,failure_reason,generation_ms,pipeline_ms,open_percent,open_tiles,logical_components,main_logical_share,navigation_components,navigation_cells,player_clear_logical_centers,player_clear_navigation,player_component_cells,enemy_valid_navigation,floor_runs,median_floor_width,platform_scale_floor_runs,sloped_floor_surfaces,landing_sites,landing_floor_runs,elevation_bands,elevation_span,bottlenecks,enemy_candidates,enemy_min_spacing,enemy_min_player_distance,enemy_horizontal_span,enemy_vertical_span");
    foreach (var item in outcomes.OrderBy(item => item.Profile).ThenBy(item => item.Seed))
    {
        var metrics = item.Metrics;
        builder.AppendLine(string.Join(',',
            Csv(item.Profile),
            Csv(item.Seed),
            item.Success,
            item.GenerationSucceeded,
            item.TopologySucceeded,
            item.PlayerSpawnSucceeded,
            item.EnemySpawnSucceeded,
            item.EnemySpawnCount,
            Csv(item.FailureStage),
            Csv(item.FailureReason),
            F(item.GenerationMilliseconds),
            F(item.PipelineMilliseconds),
            F(metrics?.OpenSpacePercentage),
            F(metrics?.OpenTileCount),
            F(metrics?.LogicalComponentCount),
            F(metrics?.MainLogicalComponentShare),
            F(metrics?.NavigationComponentCount),
            F(metrics?.NavigationCellCount),
            F(metrics?.PlayerClearLogicalTileCenterCount),
            F(metrics?.PlayerClearNavigationCellCount),
            F(metrics?.PlayerComponentCellCount),
            F(metrics?.EnemyValidNavigationCellCount),
            F(metrics?.FloorRunCount),
            F(metrics?.MedianFloorWidth),
            F(metrics?.PlatformScaleFloorRunCount),
            F(metrics?.SlopedFloorSurfaceCount),
            F(metrics?.LandingSiteCount),
            F(metrics?.LandingFloorRunCount),
            F(metrics?.LandingElevationBandCount),
            F(metrics?.LandingElevationSpan),
            F(metrics?.BottleneckCount),
            F(metrics?.EnemyCandidateCount),
            F(metrics?.EnemyMinimumSpacing),
            F(metrics?.EnemyMinimumPlayerDistance),
            F(metrics?.EnemyHorizontalSpan),
            F(metrics?.EnemyVerticalSpan)));
    }
    return builder.ToString();

    static string F(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
    static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.IndexOfAny([',', '"', '\n', '\r']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

static string BuildMarkdownReport(
    IReadOnlyList<ProfileSpec> profiles,
    IReadOnlyDictionary<string, List<EvaluationOutcome>> outcomes,
    IReadOnlyList<ProfileComparison> comparisons,
    IReadOnlyList<string> imagePaths)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Milestone 10G generation comparison");
    builder.AppendLine();
    builder.AppendLine("Generated from the fixed `m10-quality-0000` through `m10-quality-0255` corpus.");
    builder.AppendLine("Every profile is evaluated once per seed with no retry or filtering; failures remain in the CSV and the failure table below.");
    builder.AppendLine();
    builder.AppendLine("| Profile | Fill | Smooth | Wall threshold | Room threshold | Passage radius | Successful pipelines |");
    builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
    foreach (var profile in profiles)
        builder.AppendLine($"| {profile.DisplayName} | {profile.Options.RandomFillPercent}% | {profile.Options.SmoothingPasses} | {profile.Options.WallRegionThreshold} | {profile.Options.RoomRegionThreshold} | {profile.Options.PassageRadius} | {outcomes[profile.Name].Count(item => item.Success)}/256 |");
    builder.AppendLine();
    builder.AppendLine("Parameter semantics are the existing generator semantics: initial interior wall probability, count of in-place cellular smoothing passes, minimum retained wall-region size, minimum retained open-room size, and circular room-connection carving radius.");
    builder.AppendLine();
    builder.AppendLine("## Aggregate medians");
    builder.AppendLine();
    builder.AppendLine($"| Metric | {string.Join(" | ", profiles.Select(profile => profile.DisplayName))} |");
    builder.AppendLine($"|---|{string.Join("|", profiles.Select(_ => "---:"))}|");
    AddMetric("Open space %", item => item.OpenSpacePercentage);
    AddMetric("Open tiles", item => item.OpenTileCount);
    AddMetric("Logical components", item => item.LogicalComponentCount);
    AddMetric("Main logical share %", item => item.MainLogicalComponentShare * 100d);
    AddMetric("Navigation components", item => item.NavigationComponentCount);
    AddMetric("Navigation cells", item => item.NavigationCellCount);
    AddMetric("Player-clear logical centers", item => item.PlayerClearLogicalTileCenterCount);
    AddMetric("Player-clear navigation cells", item => item.PlayerClearNavigationCellCount);
    AddMetric("Floor runs", item => item.FloorRunCount);
    AddMetric("Median floor width", item => item.MedianFloorWidth);
    AddMetric("Platform-scale floor runs", item => item.PlatformScaleFloorRunCount);
    AddMetric("Sloped floor surfaces", item => item.SlopedFloorSurfaceCount);
    AddMetric("Landing sites", item => item.LandingSiteCount);
    AddMetric("Landing-bearing floor runs", item => item.LandingFloorRunCount);
    AddMetric("Elevation bands", item => item.LandingElevationBandCount);
    AddMetric("Elevation span", item => item.LandingElevationSpan);
    AddMetric("Approximate bottlenecks", item => item.BottleneckCount);
    AddOutcomeMetric("Generation ms", item => item.GenerationMilliseconds);
    AddOutcomeMetric("Full pipeline ms", item => item.PipelineMilliseconds);
    builder.AppendLine();
    builder.AppendLine("## Validation outcomes");
    builder.AppendLine();
    builder.AppendLine("| Profile | Generated | Topology valid | Player spawn valid | 15-bird spawn valid |");
    builder.AppendLine("|---|---:|---:|---:|---:|");
    foreach (var profile in profiles)
    {
        var profileOutcomes = outcomes[profile.Name];
        builder.AppendLine($"| {profile.DisplayName} | {profileOutcomes.Count(item => item.GenerationSucceeded)}/256 | {profileOutcomes.Count(item => item.TopologySucceeded)}/256 | {profileOutcomes.Count(item => item.PlayerSpawnSucceeded)}/256 | {profileOutcomes.Count(item => item.EnemySpawnSucceeded)}/256 |");
    }
    builder.AppendLine();
    builder.AppendLine("## Candidate deltas");
    builder.AppendLine();
    builder.AppendLine("Paired medians are candidate minus reference over seeds where both sides produced metrics.");
    builder.AppendLine();
    builder.AppendLine("| Candidate | Reference | Open % | Nav cells | Floor runs | Elevation bands | Bottlenecks | Pipeline ms |");
    builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
    foreach (var comparison in comparisons)
        builder.AppendLine(FormattableString.Invariant(
            $"| {comparison.Candidate.DisplayName} | {comparison.Reference.DisplayName} | {MedianMetricDelta(comparison.Pairs, item => item.OpenSpacePercentage):+0.0000;-0.0000;0.0000} | {MedianMetricDelta(comparison.Pairs, item => item.NavigationCellCount):+0.0000;-0.0000;0.0000} | {MedianMetricDelta(comparison.Pairs, item => item.FloorRunCount):+0.0000;-0.0000;0.0000} | {MedianMetricDelta(comparison.Pairs, item => item.LandingElevationBandCount):+0.0000;-0.0000;0.0000} | {MedianMetricDelta(comparison.Pairs, item => item.BottleneckCount):+0.0000;-0.0000;0.0000} | {MedianOutcomeDelta(comparison.Pairs, item => item.PipelineMilliseconds):+0.0000;-0.0000;0.0000} |"));
    builder.AppendLine();
    builder.AppendLine("## Recommendation");
    builder.AppendLine();
    builder.AppendLine(BuildRecommendation(profiles, outcomes));
    builder.AppendLine();
    builder.AppendLine("This comparison remains an offline experiment. The measured candidates are selectable for controlled testing, but baseline remains the runtime default. No shelves or artificial platforms are generated.");
    builder.AppendLine();
    builder.AppendLine("## Hard failures");
    builder.AppendLine();
    var failures = profiles.SelectMany(profile => outcomes[profile.Name]).Where(item => !item.Success).ToArray();
    if (failures.Length == 0)
        builder.AppendLine("None.");
    else
    {
        builder.AppendLine("| Profile | Seed | Stage | Reason |");
        builder.AppendLine("|---|---|---|---|");
        foreach (var failure in failures)
            builder.AppendLine($"| {failure.Profile} | {failure.Seed} | {failure.FailureStage} | {Markdown(failure.FailureReason)} |");
    }
    builder.AppendLine();
    builder.AppendLine("## Representative comparisons");
    foreach (var imagePath in imagePaths)
        builder.AppendLine($"- [{Path.GetFileName(imagePath)}]({Path.GetFileName(imagePath)})");
    return builder.ToString();

    void AddMetric(string name, Func<EvaluationMetrics, double> selector) =>
        builder.AppendLine($"| {name} | {string.Join(" | ", profiles.Select(profile => F(MedianMetrics(outcomes[profile.Name], selector))))} |");

    void AddOutcomeMetric(string name, Func<EvaluationOutcome, double> selector) =>
        builder.AppendLine($"| {name} | {string.Join(" | ", profiles.Select(profile => F(Distribution.From(outcomes[profile.Name].Select(selector)).Median)))} |");

    static double MedianMetrics(IEnumerable<EvaluationOutcome> source, Func<EvaluationMetrics, double> selector) =>
        Distribution.From(source.Where(item => item.Metrics is not null).Select(item => selector(item.Metrics!))).Median;

    static double MedianMetricDelta(IReadOnlyList<PairedOutcome> pairs, Func<EvaluationMetrics, double> selector) =>
        Distribution.From(pairs.Where(pair => pair.Reference.Metrics is not null && pair.Candidate.Metrics is not null)
            .Select(pair => selector(pair.Candidate.Metrics!) - selector(pair.Reference.Metrics!))).Median;

    static double MedianOutcomeDelta(IReadOnlyList<PairedOutcome> pairs, Func<EvaluationOutcome, double> selector) =>
        Distribution.From(pairs.Select(pair => selector(pair.Candidate) - selector(pair.Reference))).Median;

    static string F(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
    static string Markdown(string? value) => (value ?? string.Empty)
        .Replace("|", "\\|")
        .Replace("\r", " ")
        .Replace("\n", " ");
}

static string BuildRecommendation(
    IReadOnlyList<ProfileSpec> profiles,
    IReadOnlyDictionary<string, List<EvaluationOutcome>> outcomes)
{
    var candidates = profiles.Where(profile => profile.IsCandidate).ToArray();
    var fullyValid = candidates.Where(profile => outcomes[profile.Name].All(item => item.Success)).ToArray();
    if (fullyValid.Length == 0)
    {
        var best = candidates.OrderByDescending(profile => outcomes[profile.Name].Count(item => item.Success)).First();
        return $"No candidate completed all 256 full pipelines. `{best.Name}` had the most successful pipelines " +
               $"({outcomes[best.Name].Count(item => item.Success)}/256), but the measured failures should be reviewed before any further consideration. Keep baseline as the runtime default.";
    }

    var selected = fullyValid
        .OrderByDescending(profile => Distribution.From(outcomes[profile.Name]
            .Where(item => item.Metrics is not null).Select(item => OpportunityScore(item.Metrics!))).Median)
        .ThenByDescending(profile => Distribution.From(outcomes[profile.Name]
            .Where(item => item.Metrics is not null).Select(item => (double)item.Metrics!.NavigationCellCount)).Median)
        .ThenBy(profile => profile.Name, StringComparer.Ordinal)
        .First();
    var baseline = outcomes["baseline"];
    var conservative = outcomes["conservative-irregular"];
    var selectedOutcomes = outcomes[selected.Name];
    var navigationMedian = MetricMedian(selectedOutcomes, item => item.NavigationCellCount);
    var floorMedian = MetricMedian(selectedOutcomes, item => item.FloorRunCount);
    var elevationMedian = MetricMedian(selectedOutcomes, item => item.LandingElevationBandCount);
    var bottleneckMedian = MetricMedian(selectedOutcomes, item => item.BottleneckCount);
    var runtimeMedian = Distribution.From(selectedOutcomes.Select(item => item.PipelineMilliseconds)).Median;
    return string.Concat(
        $"Among candidates with 256/256 valid full pipelines, `{selected.Name}` has the strongest median floor/elevation opportunity score. " +
        $"Its medians are {floorMedian:F4} floor runs, {elevationMedian:F4} occupied landing-elevation bands, {bottleneckMedian:F4} approximate bottlenecks, " +
        $"{navigationMedian:F4} navigation cells, and {runtimeMedian:F4} ms full-pipeline runtime. " +
        $"Navigation-cell deltas are {navigationMedian - MetricMedian(baseline, item => item.NavigationCellCount):+0.0000;-0.0000;0.0000} versus baseline and " +
        $"{navigationMedian - MetricMedian(conservative, item => item.NavigationCellCount):+0.0000;-0.0000;0.0000} versus conservative-irregular. " +
        "Advance it only to host playtesting; keep baseline as the runtime default.");

    static double MetricMedian(IEnumerable<EvaluationOutcome> source, Func<EvaluationMetrics, double> selector) =>
        Distribution.From(source.Where(item => item.Metrics is not null).Select(item => selector(item.Metrics!))).Median;
}

static bool FailsBaselineSpecificThresholds(EvaluationOutcome outcome)
{
    if (outcome.Metrics is not { } report) return true;
    return report.OpenSpacePercentage < 75d || report.OpenSpacePercentage > 87d ||
           report.MainLogicalComponentShare < 0.99d ||
           report.LandingSiteCount < 350 ||
           report.PlayerClearNavigationCellCount < 2500 ||
           report.EnemyValidNavigationCellCount < 2500;
}

static string FindRepositoryRoot()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BirdHell.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("Could not locate BirdHell.sln.");
}

static void ValidateInvalidOptions()
{
    ExpectInvalid(new CaveGenerationOptions { Width = 0 });
    ExpectInvalid(new CaveGenerationOptions { Height = 0 });
    ExpectInvalid(new CaveGenerationOptions { RandomFillPercent = -1 });
    ExpectInvalid(new CaveGenerationOptions { RandomFillPercent = 101 });
    ExpectInvalid(new CaveGenerationOptions { SmoothingPasses = -1 });
    ExpectInvalid(new CaveGenerationOptions { WallRegionThreshold = -1 });
    ExpectInvalid(new CaveGenerationOptions { RoomRegionThreshold = -1 });
    ExpectInvalid(new CaveGenerationOptions { PassageRadius = -1 });

    static void ExpectInvalid(CaveGenerationOptions options)
    {
        try
        {
            _ = new CaveGenerator(options);
            throw new InvalidOperationException("Invalid cave generation options were accepted.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }
}

static void AssertOptions(
    CaveGenerationOptions options,
    int width,
    int height,
    int fill,
    int smoothing,
    int wallThreshold,
    int roomThreshold,
    int passageRadius)
{
    Assert(options.Width == width && options.Height == height && options.RandomFillPercent == fill &&
           options.SmoothingPasses == smoothing && options.WallRegionThreshold == wallThreshold &&
           options.RoomRegionThreshold == roomThreshold && options.PassageRadius == passageRadius,
        "Cave generation profile values differ from the requested configuration.");
}

static void ValidateDeterminismAndRngIsolation(IReadOnlyList<ProfileSpec> profiles)
{
    const string seed = "m10g-determinism-proof";
    var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var profile in profiles)
    {
        var generator = new CaveGenerator(profile.Options);
        var first = MapFingerprint(generator.Generate(seed));
        var repeated = MapFingerprint(generator.Generate(seed));
        Assert(first == repeated, $"Profile {profile.Name} is not repeatable for the same seed.");
        fingerprints.Add(profile.Name, first);
    }

    foreach (var profile in profiles.Reverse())
    {
        var afterOtherProfiles = MapFingerprint(new CaveGenerator(profile.Options).Generate(seed));
        Assert(afterOtherProfiles == fingerprints[profile.Name],
            $"Profile {profile.Name} changed when evaluated in a different profile order.");
    }

    foreach (var candidate in profiles.Where(profile => profile.IsCandidate))
    {
        var resolved = CaveGenerationProfiles.Resolve(candidate.Name);
        Assert(resolved.Name == candidate.Name && resolved.Options == candidate.Options,
            $"Runtime profile {candidate.Name} does not reuse the measured candidate options.");
    }
}

static PairedOutcome[] Pair(
    IReadOnlyList<EvaluationOutcome> reference,
    IReadOnlyList<EvaluationOutcome> candidate)
{
    Assert(reference.Count == candidate.Count, "Profile comparison counts differ.");
    var pairs = reference.Zip(candidate, (left, right) => new PairedOutcome(left, right)).ToArray();
    Assert(pairs.All(pair => pair.Reference.Seed == pair.Candidate.Seed),
        "Profile results are not paired by identical seed.");
    return pairs;
}

static string MapFingerprint(CaveMap map)
{
    var builder = new StringBuilder(map.Width * map.Height);
    for (var y = 0; y < map.Height; y++)
    for (var x = 0; x < map.Width; x++)
        builder.Append(map[x, y] == CaveTile.Wall ? '1' : '0');
    return builder.ToString();
}

static double Percentile(IReadOnlyList<double> ordered, double fraction) =>
    ordered[(int)Math.Round((ordered.Count - 1) * fraction)];

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Pass(string message) => Console.WriteLine($"PASS: {message}");

sealed record EvaluationOutcome(
    string Profile,
    string Seed,
    bool Success,
    string? FailureStage,
    string? FailureReason,
    double GenerationMilliseconds,
    double PipelineMilliseconds,
    EvaluationMetrics? Metrics)
{
    public bool GenerationSucceeded => Success || FailureStage != "generation";
    public bool TopologySucceeded => Success || FailureStage is "player-spawn" or "enemy-spawn";
    public bool PlayerSpawnSucceeded => Success || FailureStage == "enemy-spawn";
    public bool EnemySpawnSucceeded => Success;
    public int EnemySpawnCount => Success ? EnemySpawnSystem.OriginalEnemyCount : 0;

    public static EvaluationOutcome Succeeded(
        string profile, string seed, double generationMilliseconds, double pipelineMilliseconds, EvaluationMetrics metrics) =>
        new(profile, seed, true, null, null, generationMilliseconds, pipelineMilliseconds, metrics);

    public static EvaluationOutcome Failed(
        string profile,
        string seed,
        string stage,
        string reason,
        double generationMilliseconds,
        double pipelineMilliseconds,
        EvaluationMetrics? metrics = null) =>
        new(profile, seed, false, stage, reason, generationMilliseconds, pipelineMilliseconds, metrics);
}

sealed record EvaluationMetrics(
    double OpenSpacePercentage,
    int OpenTileCount,
    int LogicalComponentCount,
    double MainLogicalComponentShare,
    int NavigationComponentCount,
    int NavigationCellCount,
    int PlayerClearLogicalTileCenterCount,
    int PlayerComponentCellCount,
    int FloorRunCount,
    double MedianFloorWidth,
    int PlatformScaleFloorRunCount,
    int SlopedFloorSurfaceCount,
    int LandingSiteCount,
    int LandingFloorRunCount,
    int LandingElevationBandCount,
    double LandingElevationSpan,
    int BottleneckCount,
    int PlayerClearNavigationCellCount,
    int EnemyValidNavigationCellCount,
    int EnemyCandidateCount,
    double EnemyMinimumSpacing,
    double EnemyMinimumPlayerDistance,
    double EnemyHorizontalSpan,
    double EnemyVerticalSpan);

sealed record ProfileSpec(
    string Name,
    string DisplayName,
    CaveGenerationOptions Options,
    bool IsCandidate);

sealed record PairedOutcome(EvaluationOutcome Reference, EvaluationOutcome Candidate);

sealed record ProfileComparison(
    ProfileSpec Reference,
    ProfileSpec Candidate,
    IReadOnlyList<PairedOutcome> Pairs);

sealed record PipelineSnapshot(
    string Profile,
    CaveMap Map,
    CaveGeometry Geometry,
    CaveTopologyReport Topology,
    PlayerSpawnSelection Player,
    EnemySpawnSelection Enemies);

readonly record struct Distribution(int Count, double Minimum, double Median, double Maximum, double Mean)
{
    public static Distribution From(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered.Length == 0
            ? new Distribution(0, 0d, 0d, 0d, 0d)
            : new Distribution(
                ordered.Length,
                ordered[0],
                ordered[(int)Math.Round((ordered.Length - 1) * 0.5)],
                ordered[^1],
                ordered.Average());
    }
}
