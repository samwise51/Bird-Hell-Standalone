using System.Numerics;
using System.Reflection;
using BirdHell.Core;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;

const float step = 0.02f;

var run = new MainScreenRun("milestone-6-initial");
Assert(run.State == RunState.Playing, "A new run did not start Playing.");
Assert(run.Player.Health == 100 && run.Player.IsAlive, "A new run did not start with a live 100-health player.");
Assert(run.Enemies.LivingCount == EnemySpawnSystem.OriginalEnemyCount, "A new run did not contain exactly 15 living enemies.");
Console.WriteLine("PASS: new run starts Playing with a live 100-health player and exactly 15 birds");

var contactEnemy = run.Enemies.Enemies[0];
contactEnemy.Position = run.Player.ColliderCenter - contactEnemy.ColliderOffset;
var contact = new EnemyCombatSystem();
for (var hit = 0; hit < 4; hit++)
{
    contactEnemy.ContactTimer = EnemyCombatSystem.ContactDelay;
    contact.Step(run.Enemies.Enemies, run.Player, step);
}
Assert(run.Player.Health == 0 && !run.Player.IsAlive, "Four normal 25-damage contacts did not kill the player.");
Assert(run.State == RunState.PlayerDead, "A dead player did not derive PlayerDead state.");
Console.WriteLine("PASS: four normal contacts kill the player and derive PlayerDead");

var deadPosition = run.Player.Position;
var deadSeed = run.Seed;
var deadPlayer = run.Player;
var shotsBeforeDeathInput = run.WeaponState.ShotsFired;
var projectilesBeforeDeathInput = run.Projectiles.Projectiles.Count;
run.Player.Velocity = new Vector2(3f, 4f);
run.Step(new PlayerInput(1f, true, true), true, step);
Assert(run.Player.Position == deadPosition && run.Player.Velocity == Vector2.Zero,
    "A dead player performed normal movement.");
Assert(run.WeaponState.ShotsFired == shotsBeforeDeathInput && run.Projectiles.Projectiles.Count == projectilesBeforeDeathInput,
    "A dead player fired.");
for (var index = 0; index < 25; index++) run.Step(new PlayerInput(1f, true, true), true, step);
Assert(run.State == RunState.PlayerDead && ReferenceEquals(run.Player, deadPlayer) && run.Seed == deadSeed,
    "PlayerDead automatically reset or regenerated.");
Console.WriteLine("PASS: PlayerDead disables movement/fire and persists without automatic reset");

run.RestartSameSeed();
Assert(run.Player.Health == 100 && run.Player.IsAlive && run.State == RunState.Playing,
    "Manual reset did not restore health, life, control, and Playing state.");
Assert(run.Player.FacingDirection == 1 && run.Player.Velocity == Vector2.Zero,
    "Manual reset did not restore right-facing zero-velocity player state.");
Console.WriteLine("PASS: manual reset restores player health, life, control, facing, velocity, and Playing");

foreach (var enemy in run.Enemies.Enemies) enemy.TakeDamage(enemy.MaximumHealth);
Assert(run.State == RunState.AllBirdsDead, "Killing the final enemy did not derive AllBirdsDead.");
var clearedSeed = run.Seed;
var clearedGeometry = run.Geometry;
var clearedPlayer = run.Player;
var clearedX = run.Player.Position.X;
run.Step(new PlayerInput(-1f, false, false), false, step);
Assert(run.Player.IsAlive && run.Player.FacingDirection == -1 && run.Player.Position.X < clearedX,
    "The living player was not controllable after AllBirdsDead.");
for (var index = 0; index < 25; index++) run.Step(new PlayerInput(0f, false, false), false, step);
Assert(run.State == RunState.AllBirdsDead && run.Enemies.LivingCount == 0,
    "AllBirdsDead spawned replacement enemies.");
Assert(run.Seed == clearedSeed && ReferenceEquals(run.Geometry, clearedGeometry) && ReferenceEquals(run.Player, clearedPlayer),
    "AllBirdsDead automatically regenerated the cave or player.");
Console.WriteLine("PASS: AllBirdsDead persists in-place with no replacements and leaves the living player controllable");

run.RestartSameSeed();
var oldPlayer = run.Player;
var oldEnemies = run.Enemies.Enemies.ToArray();
var dirtyEnemy = oldEnemies[0];
run.Player.Position += new Vector2(1f, 2f);
run.Player.Velocity = new Vector2(-7f, 5f);
run.Player.TakeDamage(25);
run.PlayerMovement.Step(run.Player, new PlayerInput(-1f, true, true), step);
dirtyEnemy.Velocity = new Vector2(5f, 6f);
dirtyEnemy.Path = [new Vector2(99f, 99f)];
dirtyEnemy.PathIndex = 1;
dirtyEnemy.RepathTimer = 0.2f;
dirtyEnemy.ContactTimer = 0.7f;
dirtyEnemy.FacingDirection = -1;
dirtyEnemy.TakeDamage(25);
run.Projectiles.Spawn(new Projectile(Vector2.Zero, Vector2.UnitX, 20f, 25, 0.34f, 0.34f, 0.28f));
var oldNavigation = run.NavigationGrid;
var oldProjectileSystem = run.Projectiles;
var sameSeed = run.Seed;
var geometryFingerprint = Fingerprint(run.Geometry);
var navigationDebugEnabled = true;

run.RestartSameSeed();
Assert(run.Seed == sameSeed, "F5/same-seed reset changed the seed.");
Assert(Fingerprint(run.Geometry) == geometryFingerprint, "Same-seed reset did not reproduce cave geometry.");
Assert(!ReferenceEquals(run.Player, oldPlayer) && run.Player.Health == 100 && run.Player.IsAlive,
    "Atomic reset retained the old player or failed to restore it.");
Assert(run.Player.Velocity == Vector2.Zero && run.Player.FacingDirection == 1 && !run.Player.IsJumping && run.Player.JumpsUsed == 0 &&
       run.Player.DamageFlashTime == 0f,
    "Atomic reset retained player movement/jump/facing/feedback transients.");
Assert(run.Enemies.LivingCount == 15 && run.Enemies.Enemies.All(enemy => !oldEnemies.Contains(enemy)),
    "Atomic reset did not create exactly 15 fresh living enemies.");
Assert(run.Enemies.Enemies.All(enemy => enemy.Health == 100 && enemy.IsAlive && enemy.Velocity == Vector2.Zero &&
       enemy.Path.Count == 0 && enemy.PathIndex == 0 && enemy.RepathTimer == EnemyNavigationSystem.RepathInterval &&
       enemy.ContactTimer == 0f && enemy.FacingDirection == 1 && enemy.DamageFlashTime == 0f),
    "Atomic reset retained stale enemy combat/navigation/feedback state.");
Assert(run.Projectiles.Projectiles.Count == 0 && !ReferenceEquals(run.Projectiles, oldProjectileSystem),
    "Atomic reset retained old projectiles or their owning system.");
Assert(run.WeaponState.ShotsFired == 0 && run.State == RunState.Playing,
    "Atomic reset retained weapon state or failed to return to Playing.");
Assert(navigationDebugEnabled, "A user debug visualization preference was unexpectedly reset.");
Assert(!ReferenceEquals(run.NavigationGrid, oldNavigation), "Atomic reset retained the old navigation grid.");
AssertNavigationMatchesGeometry(run.NavigationGrid, run.Geometry);
var selectedAfterReset = run.Enemies.LivingEnemies.FirstOrDefault();
Assert(selectedAfterReset is not null && !oldEnemies.Contains(selectedAfterReset),
    "Dynamic debug selection retained an old enemy reference.");
Console.WriteLine("PASS: same-seed atomic reset reproduces geometry and clears all player/weapon/projectile/enemy/navigation transients");

var seedBeforeNewRun = run.Seed;
var navigationBeforeNewRun = run.NavigationGrid;
run.StartNewRun();
Assert(run.Seed != seedBeforeNewRun, "R/new-seed reset did not change the generated seed.");
Assert(run.State == RunState.Playing && run.Enemies.LivingCount == 15 && run.Player.Health == 100,
    "R/new-seed reset did not produce a complete Playing run.");
Assert(!ReferenceEquals(run.NavigationGrid, navigationBeforeNewRun), "R/new-seed reset retained stale navigation.");
AssertNavigationMatchesGeometry(run.NavigationGrid, run.Geometry);
Console.WriteLine("PASS: new-seed reset changes seed and atomically installs matching geometry/navigation/gameplay state");

foreach (var enemy in run.Enemies.Enemies) enemy.TakeDamage(enemy.MaximumHealth);
run.Player.TakeDamage(run.Player.MaximumHealth);
Assert(run.State == RunState.PlayerDead,
    "PlayerDead did not take deterministic precedence over an externally-forced simultaneous all-dead condition.");

var forbiddenNames = new[] { "Level", "Difficulty", "Score", "Scene", "Checkpoint", "Lives" };
var publicLifecycleMembers = typeof(MainScreenRun).GetMembers(BindingFlags.Instance | BindingFlags.Public)
    .Select(member => member.Name)
    .ToArray();
Assert(!publicLifecycleMembers.Any(name => forbiddenNames.Any(forbidden => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase))),
    "Progression/scene state leaked into the active MainScreen lifecycle.");
Console.WriteLine("PASS: death precedence is deterministic and no scene/progression state exists");
Console.WriteLine("All Milestone 6 lifecycle checks passed.");

static string Fingerprint(CaveGeometry geometry) => string.Join('|',
    geometry.Vertices.Select(vertex => $"{vertex.X:R},{vertex.Y:R}")) + ":" +
    string.Join('|', geometry.Triangles.Select(triangle => $"{triangle.A},{triangle.B},{triangle.C}")) + ":" +
    string.Join('|', geometry.Outlines.Select(outline => string.Join(',', outline.VertexIndices)));

static void AssertNavigationMatchesGeometry(NavigationGrid actual, CaveGeometry geometry)
{
    var expected = new NavigationGrid(geometry);
    Assert(actual.Width == expected.Width && actual.Height == expected.Height, "Navigation dimensions do not match current geometry.");
    for (var y = 0; y < actual.Height; y++)
    for (var x = 0; x < actual.Width; x++)
        Assert(actual.IsWalkable(x, y) == expected.IsWalkable(x, y),
            $"Navigation cell ({x}, {y}) came from stale geometry.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
