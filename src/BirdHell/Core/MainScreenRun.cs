using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;

namespace BirdHell.Core;

/// <summary>Renderer-independent state and fixed-step lifecycle for the active procedural MainScreen.</summary>
public sealed class MainScreenRun
{
    private readonly CaveGenerator _caveGenerator = new();
    private readonly CaveGeometryBuilder _geometryBuilder = new();
    private readonly WeaponSystem _weaponSystem = new();
    private readonly EnemyCombatSystem _enemyCombat = new();

    public MainScreenRun(string seed)
    {
        StartRun(seed);
    }

    public string Seed { get; private set; } = string.Empty;
    public CaveMap CaveMap { get; private set; } = null!;
    public CaveGeometry Geometry { get; private set; } = null!;
    public TerrainCollisionSystem TerrainCollision { get; private set; } = null!;
    public PlayerMovementSystem PlayerMovement { get; private set; } = null!;
    public Player Player { get; private set; } = null!;
    public WeaponState WeaponState { get; private set; } = null!;
    public ProjectileSystem Projectiles { get; private set; } = null!;
    public NavigationGrid NavigationGrid { get; private set; } = null!;
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
        StartRun(seed);
    }

    public void RestartSameSeed() => StartRun(Seed);

    public void StartRun(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        // Build an entire replacement world before publishing any of it. A failed build leaves the
        // current run untouched, and no simulation step can observe partially-reset state.
        var caveMap = _caveGenerator.Generate(seed);
        var geometry = _geometryBuilder.Build(caveMap);
        var terrain = new TerrainCollisionSystem(geometry);
        var movement = new PlayerMovementSystem(terrain);
        var spawn = PlayerSpawnSystem.FindSafeSpawn(geometry, PlayerCollider.Original);
        var player = new Player(spawn);
        terrain.RefreshContacts(player);
        var navigationGrid = new NavigationGrid(geometry);
        var enemies = new EnemyPopulation();
        enemies.Reset(EnemySpawnSystem.Spawn(geometry, navigationGrid, terrain, seed));
        var enemyNavigation = new EnemyNavigationSystem(new AStarPathfinder(navigationGrid), terrain);
        var projectiles = new ProjectileSystem(terrain);
        var weaponState = new WeaponState();

        Seed = seed;
        CaveMap = caveMap;
        Geometry = geometry;
        TerrainCollision = terrain;
        PlayerMovement = movement;
        Player = player;
        WeaponState = weaponState;
        Projectiles = projectiles;
        NavigationGrid = navigationGrid;
        Enemies = enemies;
        EnemyNavigation = enemyNavigation;
    }

    public MainScreenStepResult Step(PlayerInput input, bool firePressed, float deltaTime)
    {
        var movementResult = PlayerMovement.Step(Player, input, deltaTime);
        var projectile = _weaponSystem.Fire(WeaponState, Player, firePressed);
        if (projectile is not null) Projectiles.Spawn(projectile);
        Projectiles.Step(deltaTime, Enemies.CreateProjectileTargets());
        EnemyNavigation.Step(Enemies.Enemies, Player.Position, deltaTime);
        _enemyCombat.Step(Enemies.Enemies, Player, deltaTime);
        return new MainScreenStepResult(movementResult.SecondJumpPerformed);
    }

    public System.Numerics.Vector2 GetAimDirection() => _weaponSystem.GetAimDirection(Player);
    public System.Numerics.Vector2 GetMuzzlePosition() => _weaponSystem.GetMuzzlePosition(Player);
}

public readonly record struct MainScreenStepResult(bool SecondJumpPerformed);
