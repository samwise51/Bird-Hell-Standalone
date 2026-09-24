using System.Reflection;
using System.Text.Json;
using BirdHell.Core;
using BirdHell.Diagnostics;
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
