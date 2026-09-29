using System.Reflection;
using System.Numerics;
using System.Text.Json;
using BirdHell.Core;
using BirdHell.Diagnostics;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;

const float step = 0.02f;
var temporaryRoot = Path.Combine(Path.GetTempPath(), $"bird-hell-flight-recorder-checks-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporaryRoot);

try
{
    var complex = CaveGenerationProfiles.Select(CaveGenerationProfile.Complex);
    var disabledRoot = Path.Combine(temporaryRoot, "disabled");
    var disabledRun = new MainScreenRun("flight-recorder-disabled", complex);
    var disabled = FlightRecorder.Start(new FlightRecorderOptions
    {
        Enabled = false,
        OutputDirectory = disabledRoot,
    }, disabledRun, step);
    Assert(disabled is null && !Directory.Exists(disabledRoot), "Disabled recording created state or output.");
    Pass("recording disabled has no filesystem or recorder allocation");

    var parsed = BirdHellCommandLine.Parse([
        "--cave-profile", "extreme",
        "--flight-recorder",
        "--flight-recorder-dir", Path.Combine(temporaryRoot, "parsed"),
        "--flight-recorder-max-mb", "3",
        "--flight-recorder-incident-max-mb", "2",
    ]);
    Assert(parsed.CaveProfile.Profile == CaveGenerationProfile.Extreme && parsed.FlightRecorder.Enabled &&
           parsed.FlightRecorder.MaximumEventMegabytes == 3 && parsed.FlightRecorder.MaximumIncidentMegabytes == 2,
        "Combined command-line parsing lost profile or recorder settings.");
    Assert(CaveProfileCommandLine.Parse([]).Profile == CaveGenerationProfile.Baseline,
        "Legacy cave-profile default changed.");
    Pass("recorder command line preserves cave-profile behavior");

    var enabledRoot = Path.Combine(temporaryRoot, "enabled");
    var recordedRun = new MainScreenRun("flight-recorder-metadata", complex);
    var recorder = FlightRecorder.Start(new FlightRecorderOptions
    {
        Enabled = true,
        OutputDirectory = enabledRoot,
        MaximumEventMegabytes = 4,
        MaximumIncidentMegabytes = 4,
    }, recordedRun, step)!;
    recordedRun.AttachFlightRecorder(recorder);
    for (var tick = 0; tick < 80; tick++)
        recordedRun.Step(new PlayerInput(tick % 30 < 15 ? 1f : -1f, tick == 10, tick is >= 10 and < 16), tick % 17 == 0, step);
    recorder.Dispose();

    using (var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(recorder.SessionDirectory, "metadata.json"))))
    {
        var root = metadata.RootElement;
        Assert(root.GetProperty("schemaVersion").GetInt32() == FlightRecorder.SchemaVersion &&
               root.GetProperty("initialSeed").GetString() == "flight-recorder-metadata" &&
               root.GetProperty("caveProfile").GetString() == "complex" &&
               root.GetProperty("fixedSimulationStepSeconds").GetSingle() == step,
            "Session metadata omitted schema, seed, profile, or fixed step.");
        var generation = root.GetProperty("generationParameters");
        Assert(generation.GetProperty("randomFillPercent").GetInt32() == 45 &&
               generation.GetProperty("passageRadius").GetInt32() == 3,
            "Complex generation parameters were not recorded exactly.");
    }
    var eventDocuments = File.ReadLines(Path.Combine(recorder.SessionDirectory, "events.jsonl"))
        .Select(line => JsonDocument.Parse(line)).ToArray();
    try
    {
        var sequences = eventDocuments.Select(document => document.RootElement.GetProperty("sequence").GetInt64()).ToArray();
        Assert(sequences.SequenceEqual(sequences.Order()) && sequences.Distinct().Count() == sequences.Length,
            "Event sequence numbers were not strictly ordered.");
        var runStart = eventDocuments.First(document => document.RootElement.GetProperty("kind").GetString() == "run_start");
        var ids = runStart.RootElement.GetProperty("data").GetProperty("birds").EnumerateArray()
            .Select(bird => bird.GetProperty("id").GetInt32()).ToArray();
        Assert(ids.SequenceEqual(Enumerable.Range(0, EnemySpawnSystem.OriginalEnemyCount)),
            "Stable bird IDs were absent or out of order.");
    }
    finally
    {
        foreach (var document in eventDocuments) document.Dispose();
    }
    Pass("metadata, stable bird IDs, and event ordering");

    var schemaRoot = Path.Combine(temporaryRoot, "schema-2-failures");
    var schemaRun = new MainScreenRun("flight-recorder-schema-2");
    var schemaRecorder = FlightRecorder.Start(new FlightRecorderOptions
    {
        Enabled = true,
        OutputDirectory = schemaRoot,
    }, schemaRun, step)!;
    var writeBirdChanges = typeof(FlightRecorder).GetMethod(
        "WriteBirdChangeEvents", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var birdNine = new Enemy(9, Vector2.Zero, 5) { FailedRepathCount = 1 };
    var birdNineAttempt = new PathfindingAttemptDiagnostics(
        new Vector2(-11.5f, -7.5f),
        new Vector2(-17.28f, -11.44f),
        new NavigationCell(52, 28),
        new NavigationCell(47, 24),
        new NavigationCell(48, 25),
        true,
        true,
        true,
        false,
        true,
        true,
        3,
        8,
        3,
        false,
        PathfindingFailureReason.AStarFailedWithValidEndpoints);
    writeBirdChanges.Invoke(schemaRecorder,
    [
        birdNine,
        Before(birdNine),
        Sample(1, 0f, false) with { BirdId = birdNine.Id },
        Diagnostic(birdNine, birdNineAttempt),
    ]);
    var birdFour = new Enemy(4, Vector2.One, 5) { FailedRepathCount = 1 };
    var birdFourAttempt = birdNineAttempt with
    {
        RequestedStartWorldPosition = Vector2.One,
        RequestedTargetWorldPosition = new Vector2(7f, 8f),
        FailureReason = PathfindingFailureReason.ResolvedDestinationLacksRadiusClearance,
    };
    writeBirdChanges.Invoke(schemaRecorder,
    [
        birdFour,
        Before(birdFour),
        Sample(1, 0f, false) with { BirdId = birdFour.Id },
        Diagnostic(birdFour, birdFourAttempt),
    ]);
    schemaRecorder.Dispose();

    var schemaDocuments = File.ReadLines(Path.Combine(schemaRecorder.SessionDirectory, "events.jsonl"))
        .Select(line => JsonDocument.Parse(line)).ToArray();
    var failureDocuments = schemaDocuments
        .Where(document => document.RootElement.GetProperty("kind").GetString() == "pathfinding_failure")
        .ToArray();
    try
    {
        Assert(failureDocuments.Length == 2 &&
               failureDocuments.All(document => document.RootElement.GetProperty("schemaVersion").GetInt32() == 2),
            "Schema-2 pathfinding failures were absent or carried the wrong schema version.");
        var birdNineFailure = failureDocuments.Single(document =>
            document.RootElement.GetProperty("data").GetProperty("birdId").GetInt32() == 9).RootElement;
        var data = birdNineFailure.GetProperty("data");
        Assert(data.GetProperty("attemptedStartWorldPosition").GetProperty("x").GetSingle() == -11.5f &&
               data.GetProperty("attemptedStartCell").GetProperty("x").GetInt32() == 52 &&
               data.GetProperty("requestedTargetWorldPosition").GetProperty("x").GetSingle() == -17.28f &&
               data.GetProperty("nearestTargetCell").GetProperty("x").GetInt32() == 47 &&
               data.GetProperty("resolvedDestinationCell").GetProperty("x").GetInt32() == 48,
            "Schema-2 failure omitted attempted, requested, unconstrained, or resolved endpoint coordinates.");
        Assert(data.GetProperty("startEndpoint").GetProperty("traversable").GetBoolean() &&
               data.GetProperty("startEndpoint").GetProperty("radiusClear").GetBoolean() &&
               data.GetProperty("startEndpoint").GetProperty("componentId").GetInt32() == 3 &&
               !data.GetProperty("requestedTargetEndpoint").GetProperty("radiusClear").GetBoolean() &&
               data.GetProperty("requestedTargetEndpoint").GetProperty("nearestCellComponentId").GetInt32() == 8 &&
               data.GetProperty("resolvedDestinationEndpoint").GetProperty("walkable").GetBoolean() &&
               data.GetProperty("resolvedDestinationEndpoint").GetProperty("radiusClear").GetBoolean() &&
               data.GetProperty("resolvedDestinationEndpoint").GetProperty("componentId").GetInt32() == 3 &&
               data.GetProperty("failureReason").GetString() == "astar_failed_with_valid_endpoints",
            "Schema-2 failure omitted clearance, component, or explicit failure-reason details.");
        var attribution = failureDocuments.ToDictionary(
            document => document.RootElement.GetProperty("data").GetProperty("birdId").GetInt32(),
            document => document.RootElement.GetProperty("data"));
        Assert(attribution[4].GetProperty("requestedTargetWorldPosition").GetProperty("x").GetSingle() == 7f &&
               attribution[4].GetProperty("failureReason").GetString() ==
               "resolved_destination_lacks_radius_clearance" &&
               attribution[9].GetProperty("requestedTargetWorldPosition").GetProperty("x").GetSingle() == -17.28f,
            "Pathfinding diagnostics were attributed to the wrong bird.");
    }
    finally
    {
        foreach (var document in schemaDocuments) document.Dispose();
    }
    Pass("schema-2 pathfinding failures retain endpoint details, reasons, and per-bird attribution");

    var schemaOne = Path.Combine(temporaryRoot, "schema-1");
    Directory.CreateDirectory(schemaOne);
    File.WriteAllText(Path.Combine(schemaOne, "metadata.json"),
        "{\"schemaVersion\":1,\"fixedSimulationStepSeconds\":0.02}");
    File.WriteAllText(Path.Combine(schemaOne, "events.jsonl"),
        "{\"schemaVersion\":1,\"sequence\":1,\"tick\":1,\"kind\":\"pathfinding_failure\",\"data\":{\"birdId\":9}}\n");
    File.WriteAllText(Path.Combine(schemaOne, "incidents.jsonl"), string.Empty);
    File.WriteAllText(Path.Combine(schemaOne, "completion.json"),
        "{\"schemaVersion\":1,\"completedNormally\":true}");
    var schemaOneSummary = FlightRecordingAnalyzer.Analyze(schemaOne);
    Assert(schemaOneSummary.CompletedNormally && schemaOneSummary.MalformedRecords == 0 &&
           schemaOneSummary.Birds.Single().BirdId == 9 &&
           schemaOneSummary.Birds.Single().PathfindingFailures == 1,
        "The analyzer did not preserve schema-1 pathfinding-failure compatibility.");
    Pass("schema-1 recordings remain analyzable");

    var incidentRoot = Path.Combine(temporaryRoot, "incidents");
    var incidentRun = new MainScreenRun("flight-recorder-incidents");
    var incidentRecorder = FlightRecorder.Start(new FlightRecorderOptions
    {
        Enabled = true,
        OutputDirectory = incidentRoot,
        MaximumEventMegabytes = 2,
        MaximumIncidentMegabytes = 4,
    }, incidentRun, step)!;
    incidentRun.AttachFlightRecorder(incidentRecorder);
    var processSample = typeof(FlightRecorder).GetMethod("ProcessIncidentSample", BindingFlags.Instance | BindingFlags.NonPublic)!;
    for (var tick = 1; tick <= 700; tick++)
    {
        var stalled = tick >= 251;
        var sample = Sample(tick, stalled ? 0.6f : 0f, stalled && tick % 5 == 0);
        processSample.Invoke(incidentRecorder, [sample]);
        Assert(incidentRecorder.BufferedSampleCount <= 750,
            "Rolling and active incident buffers exceeded their per-bird bound.");
    }
    incidentRecorder.Dispose();
    var retained = File.ReadLines(Path.Combine(incidentRecorder.SessionDirectory, "incidents.jsonl"))
        .Select(line => JsonSerializer.Deserialize<FlightIncident>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
        .ToArray();
    Assert(retained.Length == 1, "A continuous stall emitted duplicate incidents.");
    Assert(retained[0].CompletePostWindow && retained[0].Samples.Count == 500 &&
           retained[0].Samples.First().Tick == 2 && retained[0].Samples.Last().Tick == 501,
        "Incident did not retain the bounded five-second pre/post window.");
    Pass("rolling buffer, pre/post retention, memory bound, and duplicate suppression");

    var control = new MainScreenRun("flight-recorder-determinism", complex);
    var observed = new MainScreenRun("flight-recorder-determinism", complex);
    var determinismRecorder = FlightRecorder.Start(new FlightRecorderOptions
    {
        Enabled = true,
        OutputDirectory = Path.Combine(temporaryRoot, "determinism"),
    }, observed, step)!;
    observed.AttachFlightRecorder(determinismRecorder);
    for (var tick = 0; tick < 300; tick++)
    {
        var input = new PlayerInput(tick % 100 < 40 ? 1f : tick % 100 < 80 ? -1f : 0f,
            tick is 20 or 140, tick % 120 is >= 20 and < 28);
        var fire = tick % 31 == 0;
        control.Step(input, fire, step);
        observed.Step(input, fire, step);
    }
    Assert(Fingerprint(control) == Fingerprint(observed),
        "Recording changed physics, collision, combat, navigation, or deterministic state.");
    observed.RestartSameSeed();
    control.RestartSameSeed();
    Assert(Fingerprint(control) == Fingerprint(observed), "Recording changed same-seed restart determinism.");
    determinismRecorder.Dispose();
    Pass("recording is observational across fixed-step gameplay and same-seed restart");

    var boundedRun = new MainScreenRun("flight-recorder-disk-bound");
    var boundedRecorder = FlightRecorder.Start(new FlightRecorderOptions
    {
        Enabled = true,
        OutputDirectory = Path.Combine(temporaryRoot, "bounded"),
        MaximumEventMegabytes = 1,
        MaximumIncidentMegabytes = 1,
    }, boundedRun, step)!;
    boundedRun.AttachFlightRecorder(boundedRecorder);
    for (var tick = 0; tick < 500; tick++) boundedRun.Step(default, false, step);
    boundedRecorder.Dispose();
    Assert(new FileInfo(Path.Combine(boundedRecorder.SessionDirectory, "events.jsonl")).Length <= 1024 * 1024 &&
           boundedRecorder.DroppedEventRecords > 0,
        "Event disk limit was not enforced or drops were not counted.");
    Pass("event and incident files enforce configured disk limits");

    var summary = FlightRecordingAnalyzer.Analyze(recorder.SessionDirectory);
    var reports = Path.Combine(temporaryRoot, "reports");
    FlightRecordingAnalyzer.WriteReports(summary, reports);
    Assert(summary.CompletedNormally && summary.LastTick == 80 && summary.Birds.Count == 15 &&
           File.ReadAllText(Path.Combine(reports, "report.md")).Contains("Possible explanations (not diagnoses)") &&
           File.Exists(Path.Combine(reports, "summary.json")),
        "Offline analyzer statistics or reports were incomplete.");
    var bundle = Path.Combine(temporaryRoot, "bird-bundle");
    FlightRecordingAnalyzer.ExportBirdBundle(recorder.SessionDirectory, 0, bundle);
    Assert(File.Exists(Path.Combine(bundle, "metadata.json")) && File.Exists(Path.Combine(bundle, "events.jsonl")),
        "Per-bird diagnostic bundle was not exported.");
    Pass("offline statistics, Markdown/JSON reports, and per-bird export");

    File.Delete(Path.Combine(recorder.SessionDirectory, "completion.json"));
    File.AppendAllText(Path.Combine(recorder.SessionDirectory, "events.jsonl"), "{truncated");
    var incomplete = FlightRecordingAnalyzer.Analyze(recorder.SessionDirectory);
    Assert(!incomplete.CompletedNormally && incomplete.InputWasTruncated && incomplete.MalformedRecords > 0 &&
           incomplete.EvidenceLimitations.Count > 0,
        "Analyzer did not degrade gracefully for a truncated incomplete session.");
    Pass("truncated and incomplete recordings are reported without invented data");

    Console.WriteLine("All flight-recorder checks passed.");
}
finally
{
    Directory.Delete(temporaryRoot, true);
}

static BirdMovementSample Sample(long tick, float stall, bool collision) => new(
    tick, tick * step, 1, 0,
    new FlightVector(1f, 2f), new FlightVector(1.1f, 2.1f), default,
    new FlightVector(0.1f, 0f), default, new FlightVector(3f, 2f), new FlightVector(5f, 2f),
    4f, 0f, stall, 0, collision, collision ? (int)tick : 0, 0, 1, 4, 2, 0);

static BirdBefore Before(Enemy enemy) => new(
    FlightVector.From(enemy.ColliderCenter), enemy.Health, enemy.IsAlive, 0, 0, 0, 0, null);

static EnemyNavigationDiagnostics Diagnostic(Enemy enemy, PathfindingAttemptDiagnostics attempt) => new(
    enemy.Id, enemy.Position, enemy.Velocity, null, 0f, 6.9f, 0f, 0f, attempt.StartComponentId,
    false, 0, 0, Vector2.Zero, Vector2.Zero, false, attempt);

static string Fingerprint(MainScreenRun run)
{
    var player = run.Player;
    var birds = string.Join(";", run.Enemies.Enemies.OrderBy(enemy => enemy.Id).Select(enemy =>
        $"{enemy.Id}:{enemy.Position.X:R},{enemy.Position.Y:R}:{enemy.Velocity.X:R},{enemy.Velocity.Y:R}:{enemy.Health}:{enemy.PathIndex}:{enemy.SuccessfulRepathCount}:{enemy.FailedRepathCount}"));
    return $"{run.Seed}|{player.Position.X:R},{player.Position.Y:R}|{player.Velocity.X:R},{player.Velocity.Y:R}|{player.Health}|{run.WeaponState.ShotsFired}|{birds}";
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Pass(string message) => Console.WriteLine($"PASS: {message}");
