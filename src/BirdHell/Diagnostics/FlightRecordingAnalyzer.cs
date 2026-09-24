using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BirdHell.Diagnostics;

public sealed record BirdFlightStatistics(
    int BirdId,
    long Samples,
    double DistanceMoved,
    double ProgressTowardPlayer,
    int TerrainCollisions,
    double TerrainCollisionsPerSecond,
    int SuccessfulRepaths,
    int PathfindingFailures,
    int WaypointChanges,
    int RepeatedWaypointVisits,
    int StallIncidents,
    double LongestObservedStallSeconds);

public sealed record IncidentGroup(
    string Symptom,
    int LocationBinX,
    int LocationBinY,
    int Count,
    IReadOnlyList<long> IncidentIds);

public sealed record IncidentTimelineSummary(
    long IncidentId,
    int Run,
    int BirdId,
    string Trigger,
    long TriggerTick,
    double TriggerGameTimeSeconds,
    FlightVector TriggerPosition,
    int SamplesBeforeOrAtTrigger,
    int SamplesAfterTrigger,
    int PrecedingTerrainCollisions,
    double PrecedingProgressTowardPlayer,
    float StallDurationAtTriggerSeconds,
    int NearbyBirdsAtTrigger,
    bool CompletePostWindow);

public sealed record FlightAnalysisSummary(
    int AnalyzerSchemaVersion,
    string SessionDirectory,
    bool CompletedNormally,
    bool InputWasTruncated,
    int MalformedRecords,
    long LastSequence,
    long LastTick,
    int Runs,
    int Incidents,
    int IncompleteIncidents,
    int ShotsFired,
    int PlayerDamageEvents,
    int BirdDamageEvents,
    int BirdDeaths,
    long DroppedEventRecords,
    long DroppedIncidentRecords,
    long DroppedMovementSamples,
    long DroppedIncidentSamples,
    IReadOnlyList<BirdFlightStatistics> Birds,
    IReadOnlyList<IncidentTimelineSummary> IncidentTimelines,
    IReadOnlyList<IncidentGroup> IncidentGroups,
    IReadOnlyList<string> EvidenceLimitations);

public static class FlightRecordingAnalyzer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static FlightAnalysisSummary Analyze(string sessionDirectory)
    {
        var directory = Path.GetFullPath(sessionDirectory);
        var metadataPath = Path.Combine(directory, "metadata.json");
        if (!File.Exists(metadataPath)) throw new FileNotFoundException("Recording metadata.json was not found.", metadataPath);
        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var fixedStep = metadata.RootElement.GetProperty("fixedSimulationStepSeconds").GetDouble();
        var accumulators = new Dictionary<int, BirdAccumulator>();
        var malformed = 0;
        var truncated = false;
        long lastSequence = 0;
        long lastTick = 0;
        var runs = 0;
        var shots = 0;
        var playerDamage = 0;
        var birdDamage = 0;
        var birdDeaths = 0;

        var eventsPath = Path.Combine(directory, "events.jsonl");
        if (File.Exists(eventsPath))
        {
            foreach (var line in File.ReadLines(eventsPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    var sequence = root.GetProperty("sequence").GetInt64();
                    if (sequence <= lastSequence) malformed++;
                    lastSequence = Math.Max(lastSequence, sequence);
                    lastTick = Math.Max(lastTick, root.GetProperty("tick").GetInt64());
                    var kind = root.GetProperty("kind").GetString();
                    var data = root.GetProperty("data");
                    switch (kind)
                    {
                        case "run_start": runs++; break;
                        case "player_fire": shots++; break;
                        case "player_damage": playerDamage++; break;
                        case "bird_damage": birdDamage++; break;
                        case "bird_death": birdDeaths++; break;
                        case "terrain_collision": GetBird(data, accumulators).TerrainCollisions++; break;
                        case "repath": GetBird(data, accumulators).SuccessfulRepaths++; break;
                        case "pathfinding_failure": GetBird(data, accumulators).PathfindingFailures++; break;
                        case "waypoint_change":
                            var waypointBird = GetBird(data, accumulators);
                            waypointBird.WaypointChanges++;
                            if (data.TryGetProperty("waypoint", out var waypoint) && waypoint.ValueKind == JsonValueKind.Object)
                            {
                                var key = FormattableString.Invariant(
                                    $"{waypoint.GetProperty("x").GetSingle():F2},{waypoint.GetProperty("y").GetSingle():F2}");
                                if (!waypointBird.Waypoints.Add(key)) waypointBird.RepeatedWaypointVisits++;
                            }
                            break;
                        case "tick": ReadTick(data, accumulators); break;
                    }
                }
                catch (JsonException)
                {
                    malformed++;
                    truncated = true;
                }
                catch (InvalidOperationException)
                {
                    malformed++;
                }
                catch (KeyNotFoundException)
                {
                    malformed++;
                }
            }
        }
        else truncated = true;

        var incidents = ReadIncidents(directory, accumulators, ref malformed, ref truncated);
        var completionPath = Path.Combine(directory, "completion.json");
        var completedNormally = false;
        long droppedEvents = 0;
        long droppedIncidents = 0;
        long droppedMovementSamples = 0;
        long droppedIncidentSamples = 0;
        if (File.Exists(completionPath))
        {
            try
            {
                using var completion = JsonDocument.Parse(File.ReadAllText(completionPath));
                var root = completion.RootElement;
                completedNormally = root.TryGetProperty("completedNormally", out var complete) && complete.GetBoolean();
                if (root.TryGetProperty("droppedEventRecords", out var eventDrops)) droppedEvents = eventDrops.GetInt64();
                if (root.TryGetProperty("droppedIncidentRecords", out var incidentDrops)) droppedIncidents = incidentDrops.GetInt64();
                if (root.TryGetProperty("droppedMovementSamples", out var movementDrops)) droppedMovementSamples = movementDrops.GetInt64();
                if (root.TryGetProperty("droppedIncidentSamples", out var sampleDrops)) droppedIncidentSamples = sampleDrops.GetInt64();
            }
            catch (JsonException)
            {
                malformed++;
                truncated = true;
            }
        }
        else truncated = true;

        var limitations = new List<string>();
        if (!completedNormally) limitations.Add("No normal completion marker was found; the process may have crashed or been terminated.");
        if (truncated || malformed > 0) limitations.Add("One or more records were absent, malformed, or truncated; statistics are lower bounds.");
        if (droppedEvents > 0 || droppedIncidents > 0) limitations.Add("Configured disk limits dropped records; omitted activity cannot be inferred.");
        if (incidents.Count == 0) limitations.Add("No retained incident window exists, so stall-cause comparisons are unavailable.");

        var birdStats = accumulators.OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.ToStatistics(pair.Key, fixedStep)).ToArray();
        var groups = incidents
            .GroupBy(incident => new
            {
                incident.Trigger,
                X = (int)MathF.Floor(incident.TriggerPosition.X / 2f),
                Y = (int)MathF.Floor(incident.TriggerPosition.Y / 2f),
            })
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key.Trigger, StringComparer.Ordinal)
            .ThenBy(group => group.Key.X)
            .ThenBy(group => group.Key.Y)
            .Select(group => new IncidentGroup(
                group.Key.Trigger,
                group.Key.X,
                group.Key.Y,
                group.Count(),
                group.Select(item => item.IncidentId).Order().ToArray()))
            .ToArray();
        var timelines = incidents.OrderBy(incident => incident.TriggerTick).ThenBy(incident => incident.IncidentId)
            .Select(incident =>
            {
                var before = incident.Samples.Where(sample => sample.Tick <= incident.TriggerTick).ToArray();
                var trigger = incident.Samples.LastOrDefault(sample => sample.Tick <= incident.TriggerTick);
                return new IncidentTimelineSummary(
                    incident.IncidentId,
                    incident.Run,
                    incident.BirdId,
                    incident.Trigger,
                    incident.TriggerTick,
                    incident.TriggerGameTimeSeconds,
                    incident.TriggerPosition,
                    before.Length,
                    incident.Samples.Count - before.Length,
                    before.Count(sample => sample.TerrainCollision),
                    before.Sum(sample => sample.ProgressTowardPlayer),
                    trigger?.StallDurationSeconds ?? 0f,
                    trigger?.NearbyBirdCount ?? 0,
                    incident.CompletePostWindow);
            }).ToArray();

        return new FlightAnalysisSummary(
            FlightRecorder.SchemaVersion,
            directory,
            completedNormally,
            truncated,
            malformed,
            lastSequence,
            lastTick,
            runs,
            incidents.Count,
            incidents.Count(incident => !incident.CompletePostWindow),
            shots,
            playerDamage,
            birdDamage,
            birdDeaths,
            droppedEvents,
            droppedIncidents,
            droppedMovementSamples,
            droppedIncidentSamples,
            birdStats,
            timelines,
            groups,
            limitations);
    }

    public static void WriteReports(FlightAnalysisSummary summary, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions(Json) { WriteIndented = true }));
        File.WriteAllText(Path.Combine(outputDirectory, "report.md"), BuildMarkdown(summary));
    }

    public static void ExportBirdBundle(string sessionDirectory, int birdId, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        File.Copy(Path.Combine(sessionDirectory, "metadata.json"), Path.Combine(outputDirectory, "metadata.json"), true);
        var completion = Path.Combine(sessionDirectory, "completion.json");
        if (File.Exists(completion)) File.Copy(completion, Path.Combine(outputDirectory, "completion.json"), true);
        FilterEventJsonLines(Path.Combine(sessionDirectory, "events.jsonl"), Path.Combine(outputDirectory, "events.jsonl"), birdId);
        FilterJsonLines(Path.Combine(sessionDirectory, "incidents.jsonl"), Path.Combine(outputDirectory, "incidents.jsonl"),
            root => root.TryGetProperty("birdId", out var id) && id.GetInt32() == birdId);
    }

    public static string BuildMarkdown(FlightAnalysisSummary summary)
    {
        var text = new StringBuilder();
        text.AppendLine("# Bird Hell flight-recorder report").AppendLine();
        text.AppendLine("## Measured evidence").AppendLine();
        text.AppendLine($"- Completed normally: **{summary.CompletedNormally}**");
        text.AppendLine($"- Runs: **{summary.Runs}**; last tick: **{summary.LastTick}**; incidents: **{summary.Incidents}** ({summary.IncompleteIncidents} incomplete)");
        text.AppendLine($"- Combat: {summary.ShotsFired} shots, {summary.PlayerDamageEvents} player-damage events, {summary.BirdDamageEvents} bird-damage events, {summary.BirdDeaths} bird deaths");
        text.AppendLine($"- Dropped records: {summary.DroppedEventRecords} event, {summary.DroppedIncidentRecords} incident; dropped samples: {summary.DroppedMovementSamples} movement, {summary.DroppedIncidentSamples} incident").AppendLine();
        text.AppendLine("| Bird | Samples | Distance | Progress | Collisions | Collisions/s | Repaths | Path failures | Waypoint changes | Repeats | Stall incidents | Longest stall |");
        text.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var bird in summary.Birds)
            text.AppendLine(FormattableString.Invariant(
                $"| {bird.BirdId} | {bird.Samples} | {bird.DistanceMoved:F2} | {bird.ProgressTowardPlayer:F2} | {bird.TerrainCollisions} | {bird.TerrainCollisionsPerSecond:F2} | {bird.SuccessfulRepaths} | {bird.PathfindingFailures} | {bird.WaypointChanges} | {bird.RepeatedWaypointVisits} | {bird.StallIncidents} | {bird.LongestObservedStallSeconds:F2}s |"));
        text.AppendLine().AppendLine("## Incident timelines and groups").AppendLine();
        if (summary.IncidentTimelines.Count == 0) text.AppendLine("No incident windows were retained.");
        else
        {
            text.AppendLine("| Incident | Run | Bird | Trigger | Tick/time | Location | Pre/post samples | Prior collisions | Prior progress | Stall at trigger | Nearby | Complete |");
            text.AppendLine("|---:|---:|---:|---|---|---|---:|---:|---:|---:|---:|---|");
            foreach (var incident in summary.IncidentTimelines)
                text.AppendLine(FormattableString.Invariant(
                    $"| {incident.IncidentId} | {incident.Run} | {incident.BirdId} | {incident.Trigger} | {incident.TriggerTick} / {incident.TriggerGameTimeSeconds:F2}s | ({incident.TriggerPosition.X:F2}, {incident.TriggerPosition.Y:F2}) | {incident.SamplesBeforeOrAtTrigger}/{incident.SamplesAfterTrigger} | {incident.PrecedingTerrainCollisions} | {incident.PrecedingProgressTowardPlayer:F3} | {incident.StallDurationAtTriggerSeconds:F2}s | {incident.NearbyBirdsAtTrigger} | {incident.CompletePostWindow} |"));
            text.AppendLine();
        }
        foreach (var group in summary.IncidentGroups)
            text.AppendLine($"- `{group.Symptom}` near 2-unit bin ({group.LocationBinX}, {group.LocationBinY}): {group.Count} incident(s), IDs {string.Join(", ", group.IncidentIds)}");
        text.AppendLine().AppendLine("Each incident ID maps to one JSON object in `incidents.jsonl`; its ordered `samples` array is the measured pre/post timeline.");
        text.AppendLine().AppendLine("## Possible explanations (not diagnoses)").AppendLine();
        text.AppendLine("- Repeated terrain collisions with low progress can be consistent with steering into terrain, but the recording alone does not prove the navigation cause.");
        text.AppendLine("- Low progress with several nearby birds and few terrain collisions can be consistent with crowding, but nearby count does not prove physical blockage.");
        text.AppendLine("- Repeated waypoint visits or pathfinding failures identify observable routing symptoms; they do not establish an A* defect.");
        text.AppendLine().AppendLine("## Missing or insufficient information").AppendLine();
        if (summary.EvidenceLimitations.Count == 0) text.AppendLine("No structural recording gaps were detected.");
        else foreach (var limitation in summary.EvidenceLimitations) text.AppendLine($"- {limitation}");
        return text.ToString();
    }

    private static void ReadTick(JsonElement data, Dictionary<int, BirdAccumulator> accumulators)
    {
        if (!data.TryGetProperty("birds", out var birds) || birds.ValueKind != JsonValueKind.Array) return;
        foreach (var bird in birds.EnumerateArray())
        {
            var id = bird.GetProperty("birdId").GetInt32();
            var accumulator = GetBird(id, accumulators);
            accumulator.Samples++;
            accumulator.DistanceMoved += Length(bird.GetProperty("actualMovement"));
            accumulator.Progress += bird.GetProperty("progressTowardPlayer").GetDouble();
            accumulator.LongestStall = Math.Max(accumulator.LongestStall,
                bird.GetProperty("stallDurationSeconds").GetDouble());
        }
    }

    private static List<FlightIncident> ReadIncidents(
        string directory,
        Dictionary<int, BirdAccumulator> accumulators,
        ref int malformed,
        ref bool truncated)
    {
        var result = new List<FlightIncident>();
        var path = Path.Combine(directory, "incidents.jsonl");
        if (!File.Exists(path))
        {
            truncated = true;
            return result;
        }
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var incident = JsonSerializer.Deserialize<FlightIncident>(line, Json);
                if (incident is null) { malformed++; continue; }
                result.Add(incident);
                if (string.Equals(incident.Trigger, "stall", StringComparison.Ordinal))
                    GetBird(incident.BirdId, accumulators).StallIncidents++;
                else GetBird(incident.BirdId, accumulators);
            }
            catch (JsonException)
            {
                malformed++;
                truncated = true;
            }
        }
        return result;
    }

    private static BirdAccumulator GetBird(JsonElement data, Dictionary<int, BirdAccumulator> accumulators) =>
        GetBird(data.GetProperty("birdId").GetInt32(), accumulators);

    private static BirdAccumulator GetBird(int id, Dictionary<int, BirdAccumulator> accumulators)
    {
        if (!accumulators.TryGetValue(id, out var accumulator))
        {
            accumulator = new BirdAccumulator();
            accumulators.Add(id, accumulator);
        }
        return accumulator;
    }

    private static double Length(JsonElement vector)
    {
        var x = vector.GetProperty("x").GetDouble();
        var y = vector.GetProperty("y").GetDouble();
        return Math.Sqrt(x * x + y * y);
    }

    private static void FilterJsonLines(string input, string output, Func<JsonElement, bool> include)
    {
        using var writer = new StreamWriter(output, false, new UTF8Encoding(false));
        if (!File.Exists(input)) return;
        foreach (var line in File.ReadLines(input))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (include(document.RootElement)) writer.WriteLine(line);
            }
            catch (JsonException)
            {
            }
        }
    }

    private static void FilterEventJsonLines(string input, string output, int birdId)
    {
        using var writer = new StreamWriter(output, false, new UTF8Encoding(false));
        if (!File.Exists(input)) return;
        foreach (var line in File.ReadLines(input))
        {
            try
            {
                var root = JsonNode.Parse(line)?.AsObject();
                var kind = root?["kind"]?.GetValue<string>();
                var data = root?["data"]?.AsObject();
                if (root is null || data is null) continue;
                if (kind == "tick")
                {
                    var selected = data["birds"]?.AsArray()
                        .Where(node => node?["birdId"]?.GetValue<int>() == birdId)
                        .Select(node => node!.DeepClone()).ToArray() ?? [];
                    data["birds"] = new JsonArray(selected);
                    writer.WriteLine(root.ToJsonString(Json));
                }
                else if (kind == "run_start")
                {
                    var selected = data["birds"]?.AsArray()
                        .Where(node => node?["id"]?.GetValue<int>() == birdId)
                        .Select(node => node!.DeepClone()).ToArray() ?? [];
                    data["birds"] = new JsonArray(selected);
                    writer.WriteLine(root.ToJsonString(Json));
                }
                else if (kind == "session_end" || data["birdId"]?.GetValue<int>() == birdId)
                    writer.WriteLine(line);
            }
            catch (JsonException)
            {
            }
        }
    }

    private sealed class BirdAccumulator
    {
        public long Samples;
        public double DistanceMoved;
        public double Progress;
        public int TerrainCollisions;
        public int SuccessfulRepaths;
        public int PathfindingFailures;
        public int WaypointChanges;
        public int RepeatedWaypointVisits;
        public int StallIncidents;
        public double LongestStall;
        public HashSet<string> Waypoints { get; } = [];

        public BirdFlightStatistics ToStatistics(int birdId, double fixedStep) => new(
            birdId, Samples, DistanceMoved, Progress, TerrainCollisions,
            Samples > 0 && fixedStep > 0d ? TerrainCollisions / (Samples * fixedStep) : 0d,
            SuccessfulRepaths,
            PathfindingFailures, WaypointChanges, RepeatedWaypointVisits, StallIncidents, LongestStall);
    }
}
