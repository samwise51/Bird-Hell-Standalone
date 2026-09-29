using BirdHell.Entities;
using BirdHell.Diagnostics;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Analysis;
using BirdHell.World.Geometry;

namespace BirdHell.Core;

/// <summary>Renderer-independent state and fixed-step lifecycle for the active procedural MainScreen.</summary>
public sealed class MainScreenRun
{
    private readonly CaveGenerator _caveGenerator;
    private readonly CaveGeometryBuilder _geometryBuilder = new();
    private readonly CaveTopologyAnalyzer _topologyAnalyzer = new();
    private readonly WeaponSystem _weaponSystem = new();
    private readonly EnemyCombatSystem _enemyCombat = new();
    private FlightRecorder? _flightRecorder;

    public MainScreenRun(string seed)
        : this(seed, CaveGenerationProfiles.Default)
    {
    }

    public MainScreenRun(string seed, CaveGenerationProfileSelection caveProfile)
    {
        CaveProfile = caveProfile ?? throw new ArgumentNullException(nameof(caveProfile));
        _caveGenerator = new CaveGenerator(CaveProfile.Options);
        StartRun(seed);
    }

    public CaveGenerationProfileSelection CaveProfile { get; }
    public string CaveProfileName => CaveProfile.Name;
    public string Seed { get; private set; } = string.Empty;
    public CaveMap CaveMap { get; private set; } = null!;
    public CaveGeometry Geometry { get; private set; } = null!;
    public TerrainOccupancy TerrainOccupancy { get; private set; } = null!;
    public TerrainCollisionSystem TerrainCollision { get; private set; } = null!;
    public PlayerMovementSystem PlayerMovement { get; private set; } = null!;
    public Player Player { get; private set; } = null!;
    public WeaponState WeaponState { get; private set; } = null!;
    public ProjectileSystem Projectiles { get; private set; } = null!;
    public NavigationGrid NavigationGrid { get; private set; } = null!;
    public CaveTopologyReport TopologyReport { get; private set; } = null!;
    public PlayerSpawnSelection PlayerSpawn { get; private set; } = null!;
    public int PlayerNavigationComponentId => PlayerSpawn.NavigationComponentId;
    public EnemySpawnSelection EnemySpawn { get; private set; } = null!;
    public EnemyPopulation Enemies { get; private set; } = null!;
    public EnemyNavigationSystem EnemyNavigation { get; private set; } = null!;

    // Player death takes precedence if externally-forced state ever makes both terminal facts true.
    // Normal fixed-step ordering (projectiles before enemy contact) does not create that combination.
    public RunState State => !Player.IsAlive || Player.Health <= 0
        ? RunState.PlayerDead
        : Enemies.AllEnemiesDead ? RunState.AllBirdsDead : RunState.Playing;

    public void StartNewRun()
    {
        string seed;
        do seed = CaveGenerator.CreateRandomSeed();
        while (seed == Seed);
        InstallRun(seed, null, null);
        _flightRecorder?.BeginRun(this, "new_seed");
    }

    public void RestartSameSeed()
    {
        InstallRun(Seed, null, null);
        _flightRecorder?.BeginRun(this, "same_seed_restart");
    }

    public void StartRun(string seed) => StartRun(seed, null, null);

    public void StartRun(string seed, PlayerSpawnOptions? spawnOptions) => StartRun(seed, spawnOptions, null);

    public void StartRun(
        string seed,
        PlayerSpawnOptions? spawnOptions,
        EnemySpawnOptions? enemySpawnOptions)
    {
        InstallRun(seed, spawnOptions, enemySpawnOptions);
        _flightRecorder?.BeginRun(this, "explicit_seed");
    }

    public void AttachFlightRecorder(FlightRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        if (_flightRecorder is not null) throw new InvalidOperationException("A flight recorder is already attached.");
        _flightRecorder = recorder;
        recorder.BeginRun(this, "initial");
    }

    private void InstallRun(
        string seed,
        PlayerSpawnOptions? spawnOptions,
        EnemySpawnOptions? enemySpawnOptions)
    {
        ArgumentNullException.ThrowIfNull(seed);

        // Build an entire replacement world before publishing any of it. A failed build leaves the
        // current run untouched, and no simulation step can observe partially-reset state.
        var caveMap = _caveGenerator.Generate(seed);
        var geometry = _geometryBuilder.Build(caveMap);
        var terrainOccupancy = new TerrainOccupancy(geometry);
        var terrain = new TerrainCollisionSystem(terrainOccupancy);
        var movement = new PlayerMovementSystem(terrain);
        var navigationGrid = new NavigationGrid(terrainOccupancy);
        var topology = _topologyAnalyzer.Analyze(caveMap, geometry, terrainOccupancy, navigationGrid);
        var spawnResult = PlayerSpawnSystem.Select(
            topology, geometry, terrainOccupancy, terrain, navigationGrid, PlayerCollider.Original, spawnOptions);
        if (!spawnResult.IsSuccess) throw new PlayerSpawnException(spawnResult.Failure!);
        var spawn = spawnResult.Selection!;
        var player = new Player(spawn.Position);
        terrain.RefreshContacts(player);
        var enemySpawnResult = EnemySpawnSystem.Select(
            terrainOccupancy, navigationGrid, player, spawn, seed, enemySpawnOptions);
        if (!enemySpawnResult.IsSuccess) throw new EnemySpawnException(enemySpawnResult.Failure!);
        var enemySpawn = enemySpawnResult.Selection!;
        var enemies = new EnemyPopulation();
        enemies.Reset(enemySpawn.CreateEnemies());
        var enemyNavigation = new EnemyNavigationSystem(new AStarPathfinder(navigationGrid), terrain);
        var projectiles = new ProjectileSystem(terrain);
        var weaponState = new WeaponState();

        Seed = seed;
        CaveMap = caveMap;
        Geometry = geometry;
        TerrainOccupancy = terrainOccupancy;
        TerrainCollision = terrain;
        PlayerMovement = movement;
        Player = player;
        WeaponState = weaponState;
        Projectiles = projectiles;
        NavigationGrid = navigationGrid;
        TopologyReport = topology;
        PlayerSpawn = spawn;
        EnemySpawn = enemySpawn;
        Enemies = enemies;
        EnemyNavigation = enemyNavigation;
    }

    public MainScreenStepResult Step(PlayerInput input, bool firePressed, float deltaTime)
    {
        var unusedTimings = default(MainScreenStepTimings);
        var unusedNavigationTimings = default(EnemyNavigationStepTimings);
        return StepCore(input, firePressed, deltaTime, ref unusedTimings, ref unusedNavigationTimings, false);
    }

    internal MainScreenStepResult Step(
        PlayerInput input,
        bool firePressed,
        float deltaTime,
        ref MainScreenStepTimings timings,
        ref EnemyNavigationStepTimings navigationTimings) =>
        StepCore(input, firePressed, deltaTime, ref timings, ref navigationTimings, true);

    private MainScreenStepResult StepCore(
        PlayerInput input,
        bool firePressed,
        float deltaTime,
        ref MainScreenStepTimings timings,
        ref EnemyNavigationStepTimings navigationTimings,
        bool captureTimings)
    {
        var timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        var recorderBefore = _flightRecorder?.CaptureBeforeStep(this);
        if (captureTimings) timings.FlightRecorderBefore = FrameStallDiagnostics.Complete(timing);

        timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        Player.AdvanceFeedback(deltaTime);
        foreach (var enemy in Enemies.Enemies) enemy.AdvanceFeedback(deltaTime);
        if (captureTimings) timings.FeedbackAdvance = FrameStallDiagnostics.Complete(timing);

        timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        var movementResult = PlayerMovement.Step(Player, input, deltaTime);
        if (captureTimings) timings.PlayerMovement = FrameStallDiagnostics.Complete(timing);

        timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        var projectile = _weaponSystem.Fire(WeaponState, Player, firePressed);
        if (projectile is not null) Projectiles.Spawn(projectile);
        if (captureTimings) timings.Weapon = FrameStallDiagnostics.Complete(timing);

        timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        var projectileResult = Projectiles.Step(deltaTime, Enemies.CreateProjectileTargets());
        if (captureTimings) timings.Projectiles = FrameStallDiagnostics.Complete(timing);

        timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        if (captureTimings)
            EnemyNavigation.Step(Enemies.Enemies, Player.Position, deltaTime, ref navigationTimings);
        else
            EnemyNavigation.Step(Enemies.Enemies, Player.Position, deltaTime);
        if (captureTimings) timings.EnemyNavigation = FrameStallDiagnostics.Complete(timing);

        timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        var combatResult = _enemyCombat.Step(Enemies.Enemies, Player, deltaTime);
        if (captureTimings) timings.EnemyCombat = FrameStallDiagnostics.Complete(timing);

        timing = captureTimings ? FrameStallDiagnostics.Capture() : default;
        if (recorderBefore is not null)
            _flightRecorder!.RecordStep(this, recorderBefore, input, firePressed, deltaTime);
        if (captureTimings) timings.FlightRecorderAfter = FrameStallDiagnostics.Complete(timing);
        return new MainScreenStepResult(
            movementResult.SecondJumpPerformed,
            new GameplayFeedbackEvents(
                projectile is not null ? 1 : 0,
                combatResult.PlayerDamageEvents,
                combatResult.PlayerDied,
                projectileResult.DamageEvents,
                projectileResult.DeathEvents));
    }

    public System.Numerics.Vector2 GetAimDirection() => _weaponSystem.GetAimDirection(Player);
    public System.Numerics.Vector2 GetMuzzlePosition() => _weaponSystem.GetMuzzlePosition(Player);
}

public readonly record struct GameplayFeedbackEvents(
    int ProjectilesFired,
    int PlayerDamageEvents,
    bool PlayerDied,
    int EnemyDamageEvents,
    int EnemyDeaths);

public readonly record struct MainScreenStepResult(
    bool SecondJumpPerformed,
    GameplayFeedbackEvents Feedback);

internal struct MainScreenStepTimings
{
    public StallSectionTiming FlightRecorderBefore;
    public StallSectionTiming FeedbackAdvance;
    public StallSectionTiming PlayerMovement;
    public StallSectionTiming Weapon;
    public StallSectionTiming Projectiles;
    public StallSectionTiming EnemyNavigation;
    public StallSectionTiming EnemyCombat;
    public StallSectionTiming FlightRecorderAfter;
}
