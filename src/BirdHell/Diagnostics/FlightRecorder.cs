using System.Text.Json;
using BirdHell.Core;
using BirdHell.Systems;

namespace BirdHell.Diagnostics;

public sealed class FlightRecorder : IDisposable
{
    public const int SchemaVersion = 2;
    public const float StallTriggerSeconds = 0.5f;
    public const int RepeatedCollisionCount = 5;
    public const float RepeatedCollisionWindowSeconds = 1f;
    private readonly FlightRecorderOptions _options;
    private readonly float _fixedStep;
    private readonly BoundedJsonLinesWriter _events;
    private readonly BoundedJsonLinesWriter _incidents;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly Dictionary<int, BirdIncidentState> _birdStates = [];
    private bool _disposed;
    private long _tick;
    private long _sequence;
    private long _nextIncidentId = 1;
    private long _droppedMovementSamples;
    private long _droppedIncidentSamples;
    private int _run;

    private FlightRecorder(FlightRecorderOptions options, MainScreenRun initialRun, float fixedStep, string sessionDirectory)
    {
        _options = options;
        _fixedStep = fixedStep;
        SessionDirectory = sessionDirectory;
        _events = new BoundedJsonLinesWriter(Path.Combine(sessionDirectory, "events.jsonl"), options.MaximumEventMegabytes);
        _incidents = new BoundedJsonLinesWriter(Path.Combine(sessionDirectory, "incidents.jsonl"), options.MaximumIncidentMegabytes);
        var assembly = typeof(FlightRecorder).Assembly.GetName();
        var generation = initialRun.CaveProfile.Options;
        var metadata = new FlightRecorderMetadata(
            SchemaVersion,
            Path.GetFileName(sessionDirectory),
            DateTimeOffset.UtcNow,
            assembly.Name ?? "BirdHell",
            assembly.Version?.ToString(),
            fixedStep,
            "+X right, +Y up; positions are world-space entity origins unless named colliderCenter",
            "world units (one generated cave tile is one world unit), seconds, simulation ticks",
            initialRun.Seed,
            initialRun.CaveProfileName,
            new
            {
                generation.Width,
                generation.Height,
                generation.RandomFillPercent,
                generation.SmoothingPasses,
                generation.WallRegionThreshold,
                generation.RoomRegionThreshold,
                generation.PassageRadius,
            },
            new
            {
                maximumEventBytes = options.MaximumEventMegabytes * 1024L * 1024L,
                maximumIncidentBytes = options.MaximumIncidentMegabytes * 1024L * 1024L,
                preIncidentSeconds = options.PreIncidentSeconds,
                postIncidentSeconds = options.PostIncidentSeconds,
                maximumBufferedSamplesPerBird = PreSampleCount,
            });
        File.WriteAllText(Path.Combine(sessionDirectory, "metadata.json"),
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions(_json) { WriteIndented = true }));
    }

    public string SessionDirectory { get; }
    public long DroppedEventRecords => _events.DroppedRecords;
    public long DroppedIncidentRecords => _incidents.DroppedRecords;
    public long DroppedMovementSamples => _droppedMovementSamples;
    public long DroppedIncidentSamples => _droppedIncidentSamples;
    public int BufferedSampleCount => _birdStates.Values.Sum(state => state.PreSamples.Count + (state.Active?.Samples.Count ?? 0));
    private int PreSampleCount => Math.Max(1, (int)MathF.Round(_options.PreIncidentSeconds / _fixedStep));
    private int PostSampleCount => Math.Max(1, (int)MathF.Round(_options.PostIncidentSeconds / _fixedStep));

    public static FlightRecorder? Start(FlightRecorderOptions options, MainScreenRun initialRun, float fixedStep)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(initialRun);
        if (!options.Enabled) return null;
        if (!float.IsFinite(fixedStep) || fixedStep <= 0f) throw new ArgumentOutOfRangeException(nameof(fixedStep));
        var root = options.ResolveOutputDirectory();
        Directory.CreateDirectory(root);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        while (true)
        {
            var path = Path.Combine(root, $"{stamp}-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(path);
                return new FlightRecorder(options, initialRun, fixedStep, path);
            }
            catch (IOException) when (Directory.Exists(path))
            {
            }
        }
    }

    public void BeginRun(MainScreenRun run, string reason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(run);
        foreach (var state in _birdStates.Values) FinalizeIncident(state, false);
        _run++;
        _birdStates.Clear();
        WriteEvent("run_start", new
        {
            reason,
            seed = run.Seed,
            profile = run.CaveProfileName,
            playerPosition = FlightVector.From(run.Player.Position),
            birds = run.Enemies.Enemies.OrderBy(enemy => enemy.Id).Select(enemy => new
            {
                id = enemy.Id,
                position = FlightVector.From(enemy.Position),
                maximumSpeed = enemy.MaximumSpeed,
            }).ToArray(),
        });
    }

    public FlightRecorderStepBefore CaptureBeforeStep(MainScreenRun run)
    {
        var birds = run.Enemies.Enemies.ToDictionary(
            enemy => enemy.Id,
            enemy => new BirdBefore(
                FlightVector.From(enemy.ColliderCenter),
                enemy.Health,
                enemy.IsAlive,
                enemy.PathIndex,
                enemy.Path.Count,
                enemy.SuccessfulRepathCount,
                enemy.FailedRepathCount,
                enemy.PathIndex < enemy.Path.Count ? FlightVector.From(enemy.SteeringTarget) : null));
        return new FlightRecorderStepBefore(
            FlightVector.From(run.Player.Position),
            run.Player.Health,
            run.WeaponState.ShotsFired,
            run.Projectiles.Projectiles.Count,
            birds);
    }

    public void RecordStep(
        MainScreenRun run,
        FlightRecorderStepBefore before,
        PlayerInput input,
        bool firePressed,
        float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _tick++;
        var gameTime = _tick * (double)_fixedStep;
        var diagnostics = run.EnemyNavigation.GetDiagnostics(run.Enemies.Enemies, run.Player.Position)
            .ToDictionary(item => item.EnemyId);
        var samples = new List<BirdMovementSample>(diagnostics.Count);
        foreach (var enemy in run.Enemies.Enemies.OrderBy(enemy => enemy.Id))
        {
            if (!diagnostics.TryGetValue(enemy.Id, out var diagnostic)) continue;
            var sample = new BirdMovementSample(
                _tick,
                gameTime,
                _run,
                enemy.Id,
                FlightVector.From(enemy.Position),
                FlightVector.From(enemy.ColliderCenter),
                FlightVector.From(enemy.Velocity),
                FlightVector.From(diagnostic.IntendedMovement),
                FlightVector.From(diagnostic.ActualMovement),
                diagnostic.CurrentWaypoint is { } waypoint ? FlightVector.From(waypoint) : null,
                FlightVector.From(run.Player.Position),
                diagnostic.PlayerDistance,
                diagnostic.PlayerDistanceDelta,
                diagnostic.StallDuration,
                diagnostic.NavigationComponentId,
                diagnostic.CollidedWithTerrainThisStep,
                diagnostic.TerrainCollisionCount,
                diagnostic.NearbyEnemyCount,
                enemy.PathIndex,
                enemy.Path.Count,
                enemy.SuccessfulRepathCount,
                enemy.FailedRepathCount);
            samples.Add(sample);
            ProcessIncidentSample(sample);
            WriteBirdChangeEvents(enemy, before.Birds[enemy.Id], sample, diagnostic);
        }

        WriteCombatEvents(run, before, firePressed);
        if (!WriteEvent("tick", new
        {
            deltaTime,
            input = new { input.Horizontal, input.JumpPressed, input.JumpHeld, firePressed },
            player = new
            {
                position = FlightVector.From(run.Player.Position),
                velocity = FlightVector.From(run.Player.Velocity),
                actualMovement = Difference(FlightVector.From(run.Player.Position), before.PlayerPosition),
                health = run.Player.Health,
                alive = run.Player.IsAlive,
                grounded = run.Player.IsGrounded,
            },
            birds = samples,
        })) _droppedMovementSamples += samples.Count;
    }

    private void WriteBirdChangeEvents(
        Entities.Enemy enemy,
        BirdBefore before,
        BirdMovementSample sample,
        EnemyNavigationDiagnostics diagnostic)
    {
        if (enemy.SuccessfulRepathCount != before.SuccessfulRepaths)
            WriteEvent("repath", new { birdId = enemy.Id, success = true, pathLength = enemy.Path.Count });
        if (enemy.FailedRepathCount != before.FailedRepaths)
        {
            var attempt = diagnostic.LastPathfindingAttempt;
            WriteEvent("pathfinding_failure", new
            {
                birdId = enemy.Id,
                attemptedStartWorldPosition = attempt is null
                    ? null
                    : (FlightVector?)FlightVector.From(attempt.RequestedStartWorldPosition),
                attemptedStartCell = Cell(attempt?.StartCell),
                requestedTargetWorldPosition = attempt is null
                    ? null
                    : (FlightVector?)FlightVector.From(attempt.RequestedTargetWorldPosition),
                nearestTargetCell = Cell(attempt?.NearestTargetCell),
                resolvedDestinationCell = Cell(attempt?.ResolvedDestinationCell),
                startEndpoint = attempt is null ? null : new
                {
                    traversable = attempt.StartTraversable,
                    radiusClear = attempt.StartRadiusClear,
                    componentId = attempt.StartComponentId,
                },
                requestedTargetEndpoint = attempt is null ? null : new
                {
                    traversable = attempt.RequestedTargetTraversable,
                    radiusClear = attempt.RequestedTargetRadiusClear,
                    nearestCellComponentId = attempt.NearestTargetComponentId,
                },
                resolvedDestinationEndpoint = attempt is null ? null : new
                {
                    walkable = attempt.ResolvedDestinationWalkable,
                    radiusClear = attempt.ResolvedDestinationRadiusClear,
                    componentId = attempt.ResolvedDestinationComponentId,
                },
                failureReason = attempt?.FailureReason is { } reason
                    ? FailureReason(reason)
                    : "missing_attempt_diagnostics",
            });
        }
        if (enemy.PathIndex != before.PathIndex || sample.CurrentWaypoint != before.Waypoint)
            WriteEvent("waypoint_change", new
            {
                birdId = enemy.Id,
                previousIndex = before.PathIndex,
                pathIndex = enemy.PathIndex,
                waypoint = sample.CurrentWaypoint,
            });
        if (sample.TerrainCollision)
            WriteEvent("terrain_collision", new
            {
                birdId = enemy.Id,
                sample.Position,
                sample.IntendedMovement,
                sample.ActualMovement,
                sample.CurrentWaypoint,
            });
        if (before.Alive && !enemy.IsAlive)
            WriteEvent("bird_death", new { birdId = enemy.Id, position = FlightVector.From(enemy.Position) });
        else if (enemy.Health < before.Health)
            WriteEvent("bird_damage", new { birdId = enemy.Id, damage = before.Health - enemy.Health, health = enemy.Health });
    }

    private void WriteCombatEvents(MainScreenRun run, FlightRecorderStepBefore before, bool firePressed)
    {
        if (run.WeaponState.ShotsFired > before.ShotsFired)
            WriteEvent("player_fire", new { requested = firePressed, shotsFired = run.WeaponState.ShotsFired });
        if (run.Player.Health < before.PlayerHealth)
            WriteEvent("player_damage", new
            {
                damage = before.PlayerHealth - run.Player.Health,
                health = run.Player.Health,
                alive = run.Player.IsAlive,
            });
    }

    private void ProcessIncidentSample(BirdMovementSample sample)
    {
        if (!_birdStates.TryGetValue(sample.BirdId, out var state))
        {
            state = new BirdIncidentState();
            _birdStates.Add(sample.BirdId, state);
        }

        state.PreSamples.Enqueue(sample);
        while (state.PreSamples.Count > PreSampleCount) state.PreSamples.Dequeue();
        if (sample.TerrainCollision) state.CollisionTicks.Enqueue(sample.Tick);
        var oldestCollision = sample.Tick - Math.Max(1, (long)MathF.Round(RepeatedCollisionWindowSeconds / _fixedStep));
        while (state.CollisionTicks.Count > 0 && state.CollisionTicks.Peek() < oldestCollision)
            state.CollisionTicks.Dequeue();

        if (state.Active is not null)
        {
            if (sample.Tick > state.Active.TriggerTick) state.Active.Samples.Add(sample);
            if (sample.Tick - state.Active.TriggerTick >= PostSampleCount)
            {
                FinalizeIncident(state, true);
                state.Suppressed = true;
            }
            return;
        }

        var stall = sample.StallDurationSeconds >= StallTriggerSeconds;
        var collision = state.CollisionTicks.Count >= RepeatedCollisionCount;
        if (state.Suppressed)
        {
            if (!stall && state.CollisionTicks.Count == 0) state.HealthyTicks++;
            else state.HealthyTicks = 0;
            if (state.HealthyTicks >= Math.Max(1, (int)MathF.Round(1f / _fixedStep)))
            {
                state.Suppressed = false;
                state.HealthyTicks = 0;
            }
            return;
        }
        if (!stall && !collision) return;

        state.Active = new ActiveIncident(
            _nextIncidentId++,
            collision ? "repeated_terrain_collision" : "stall",
            sample.Tick,
            sample.GameTimeSeconds,
            sample.Position,
            state.PreSamples.ToList());
    }

    private void FinalizeIncident(BirdIncidentState state, bool completePostWindow)
    {
        var active = state.Active;
        if (active is null) return;
        if (!_incidents.Write(new FlightIncident(
            SchemaVersion,
            active.Id,
            _run,
            active.Samples[0].BirdId,
            active.Trigger,
            active.TriggerTick,
            active.TriggerGameTime,
            active.TriggerPosition,
            completePostWindow,
            active.Samples), _json))
            _droppedIncidentSamples += active.Samples.Count;
        state.Active = null;
    }

    private bool WriteEvent(string kind, object data) => _events.Write(new
    {
        schemaVersion = SchemaVersion,
        sequence = ++_sequence,
        tick = _tick,
        gameTimeSeconds = _tick * (double)_fixedStep,
        run = _run,
        kind,
        data,
    }, _json);

    private static FlightVector Difference(FlightVector end, FlightVector start) =>
        new(end.X - start.X, end.Y - start.Y);

    private static object? Cell(BirdHell.World.NavigationCell? cell) => cell is { } value
        ? new { value.X, value.Y }
        : null;

    private static string FailureReason(PathfindingFailureReason reason) => reason switch
    {
        PathfindingFailureReason.InvalidStartWorldPosition => "invalid_start_world_position",
        PathfindingFailureReason.StartLacksRadiusClearance => "start_lacks_radius_clearance",
        PathfindingFailureReason.NoStartCell => "no_start_cell",
        PathfindingFailureReason.NoReachableDestinationCell => "no_reachable_destination_cell",
        PathfindingFailureReason.ResolvedDestinationLacksRadiusClearance =>
            "resolved_destination_lacks_radius_clearance",
        PathfindingFailureReason.AStarFailedWithValidEndpoints => "astar_failed_with_valid_endpoints",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var state in _birdStates.Values) FinalizeIncident(state, false);
        WriteEvent("session_end", new { completedNormally = true });
        var completion = new
        {
            schemaVersion = SchemaVersion,
            completedNormally = true,
            endedUtc = DateTimeOffset.UtcNow,
            ticks = _tick,
            runs = _run,
            eventBytes = _events.BytesWritten,
            incidentBytes = _incidents.BytesWritten,
            droppedEventRecords = _events.DroppedRecords,
            droppedIncidentRecords = _incidents.DroppedRecords,
            droppedMovementSamples = _droppedMovementSamples,
            droppedIncidentSamples = _droppedIncidentSamples,
        };
        File.WriteAllText(Path.Combine(SessionDirectory, "completion.json"),
            JsonSerializer.Serialize(completion, new JsonSerializerOptions(_json) { WriteIndented = true }));
        _events.Dispose();
        _incidents.Dispose();
        _disposed = true;
    }

    private sealed class BirdIncidentState
    {
        public Queue<BirdMovementSample> PreSamples { get; } = [];
        public Queue<long> CollisionTicks { get; } = [];
        public ActiveIncident? Active { get; set; }
        public bool Suppressed { get; set; }
        public int HealthyTicks { get; set; }
    }

    private sealed record ActiveIncident(
        long Id,
        string Trigger,
        long TriggerTick,
        double TriggerGameTime,
        FlightVector TriggerPosition,
        List<BirdMovementSample> Samples);
}
