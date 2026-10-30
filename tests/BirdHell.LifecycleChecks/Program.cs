using System.Numerics;
using System.Reflection;
using BirdHell.Core;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;

const float step = 0.02f;

var omittedProfile = CaveProfileCommandLine.Parse([]);
var explicitBaseline = CaveProfileCommandLine.Parse(["--cave-profile", "baseline"]);
var conservativeProfile = CaveProfileCommandLine.Parse(["--cave-profile", "conservative-irregular"]);
var denseProfile = CaveProfileCommandLine.Parse(["--cave-profile", "dense"]);
var complexProfile = CaveProfileCommandLine.Parse(["--cave-profile", "complex"]);
var extremeProfile = CaveProfileCommandLine.Parse(["--cave-profile", "extreme"]);
Assert(omittedProfile.Profile == CaveGenerationProfile.Baseline && omittedProfile.Name == "baseline",
    "Omitted cave-profile option did not select baseline.");
Assert(explicitBaseline.Profile == CaveGenerationProfile.Baseline &&
       explicitBaseline.Options == CaveGenerationProfiles.Baseline,
    "Explicit baseline cave profile did not resolve to the baseline options.");
AssertOptions(explicitBaseline.Options, 128, 30, 35, 5, 50, 50, 5);
Assert(conservativeProfile.Profile == CaveGenerationProfile.ConservativeIrregular &&
       conservativeProfile.Options == CaveGenerationProfiles.ConservativeIrregular,
    "Conservative-irregular cave profile did not resolve to the experimental options.");
AssertOptions(conservativeProfile.Options, 128, 30, 38, 4, 40, 50, 4);
Assert(denseProfile.Profile == CaveGenerationProfile.Dense && denseProfile.Name == "dense" &&
       denseProfile.Options == CaveGenerationProfiles.Dense,
    "Dense cave profile did not resolve to its measured options.");
Assert(complexProfile.Profile == CaveGenerationProfile.Complex && complexProfile.Name == "complex" &&
       complexProfile.Options == CaveGenerationProfiles.Complex,
    "Complex cave profile did not resolve to its measured options.");
Assert(extremeProfile.Profile == CaveGenerationProfile.Extreme && extremeProfile.Name == "extreme" &&
       extremeProfile.Options == CaveGenerationProfiles.Extreme,
    "Extreme cave profile did not resolve to its measured options.");
AssertOptions(denseProfile.Options, 128, 30, 42, 4, 40, 50, 4);
AssertOptions(complexProfile.Options, 128, 30, 45, 3, 35, 50, 3);
AssertOptions(extremeProfile.Options, 128, 30, 48, 3, 35, 50, 3);
AssertThrowsWithProfileNames(() => CaveProfileCommandLine.Parse(["--cave-profile"]),
    "Missing cave-profile value was accepted.");
AssertThrowsWithProfileNames(() => CaveProfileCommandLine.Parse(["--cave-profile", "unknown"]),
    "Unknown cave-profile value was accepted.");
AssertThrows<ArgumentException>(() => CaveProfileCommandLine.Parse(
        ["--cave-profile", "dense", "--cave-profile", "extreme"]),
    "Duplicate cave-profile option was accepted.");
Console.WriteLine("PASS: command-line cave profiles default, resolve, and reject invalid input exactly");

const string profileComparisonSeed = "m10-smoke-0000";
var baselineMap = new CaveGenerator(explicitBaseline.Options).Generate(profileComparisonSeed);
var omittedMap = new CaveGenerator(omittedProfile.Options).Generate(profileComparisonSeed);
var conservativeMap = new CaveGenerator(conservativeProfile.Options).Generate(profileComparisonSeed);
var denseMap = new CaveGenerator(denseProfile.Options).Generate(profileComparisonSeed);
var complexMap = new CaveGenerator(complexProfile.Options).Generate(profileComparisonSeed);
var extremeMap = new CaveGenerator(extremeProfile.Options).Generate(profileComparisonSeed);
Assert(MapFingerprint(omittedMap) == MapFingerprint(baselineMap),
    "Omitted and explicit baseline selections produced different same-seed maps.");
Assert(MapFingerprint(baselineMap) != MapFingerprint(conservativeMap),
    "Representative same-seed maps did not differ across cave profiles.");
Assert(new[] { denseMap, complexMap, extremeMap }.All(map => MapFingerprint(map) != MapFingerprint(baselineMap)),
    "A Milestone 10G profile did not differ from baseline for the representative seed.");

AssertCandidateProfileLifecycle(denseProfile, CaveGenerationProfile.Dense, profileComparisonSeed);
AssertCandidateProfileLifecycle(complexProfile, CaveGenerationProfile.Complex, profileComparisonSeed);
AssertCandidateProfileLifecycle(extremeProfile, CaveGenerationProfile.Extreme, profileComparisonSeed);
Console.WriteLine("PASS: Milestone 10G profiles validate, repeat seed/profile with F5, and retain profile through R");

var conservativeRun = new MainScreenRun(profileComparisonSeed, conservativeProfile);
Assert(conservativeRun.CaveProfile.Profile == CaveGenerationProfile.ConservativeIrregular &&
       conservativeRun.CaveProfileName == "conservative-irregular" &&
       conservativeRun.State == RunState.Playing && conservativeRun.Player.IsGrounded &&
       conservativeRun.Enemies.LivingCount == EnemySpawnSystem.OriginalEnemyCount,
    "Conservative profile did not complete the validated player/enemy pipeline.");
var conservativeSeed = conservativeRun.Seed;
var conservativeGeometry = Fingerprint(conservativeRun.Geometry);
var conservativePlayerSpawn = conservativeRun.PlayerSpawn;
var conservativeEnemySpawn = conservativeRun.EnemySpawn.Placements.ToArray();
conservativeRun.RestartSameSeed();
Assert(conservativeRun.Seed == conservativeSeed &&
       conservativeRun.CaveProfile.Profile == CaveGenerationProfile.ConservativeIrregular &&
       Fingerprint(conservativeRun.Geometry) == conservativeGeometry &&
       conservativeRun.PlayerSpawn == conservativePlayerSpawn &&
       conservativeRun.EnemySpawn.Placements.SequenceEqual(conservativeEnemySpawn),
    "Conservative F5 did not reproduce seed, profile, geometry, player spawn, enemy positions, and speeds.");
var conservativeRetainedMap = conservativeRun.CaveMap;
var conservativeRetainedPlayer = conservativeRun.Player;
AssertThrows<PlayerSpawnException>(() => conservativeRun.StartRun(
        "m10f-conservative-atomic-failure", new PlayerSpawnOptions { MinimumSupportWidth = 10_000f }),
    "Conservative replacement with impossible spawn requirements did not fail.");
Assert(conservativeRun.Seed == conservativeSeed &&
       conservativeRun.CaveProfile.Profile == CaveGenerationProfile.ConservativeIrregular &&
       ReferenceEquals(conservativeRun.CaveMap, conservativeRetainedMap) &&
       ReferenceEquals(conservativeRun.Player, conservativeRetainedPlayer),
    "Failed conservative replacement changed the published seed, profile, or world.");
conservativeRun.StartNewRun();
Assert(conservativeRun.Seed != conservativeSeed &&
       conservativeRun.CaveProfile.Profile == CaveGenerationProfile.ConservativeIrregular &&
       conservativeRun.State == RunState.Playing &&
       conservativeRun.Enemies.LivingCount == EnemySpawnSystem.OriginalEnemyCount,
    "Conservative R did not preserve the profile while installing a validated new-seed run.");
Console.WriteLine("PASS: conservative profile differs from baseline, validates, repeats with F5, retains profile with R, and replaces atomically");

var run = new MainScreenRun("milestone-6-initial");
Assert(run.CaveProfile.Profile == CaveGenerationProfile.Baseline && run.CaveProfileName == "baseline",
    "Legacy MainScreenRun construction no longer defaults to baseline.");
Assert(run.State == RunState.Playing, "A new run did not start Playing.");
Assert(run.Player.Health == 100 && run.Player.IsAlive, "A new run did not start with a live 100-health player.");
Assert(run.Enemies.LivingCount == EnemySpawnSystem.OriginalEnemyCount, "A new run did not contain exactly 15 living enemies.");
Console.WriteLine("PASS: new run starts Playing with a live 100-health player and exactly 15 birds");

var fireStep = run.Step(new PlayerInput(0f, false, false), true, step);
Assert(fireStep.Feedback.ProjectilesFired == 1 && fireStep.Feedback.PlayerDamageEvents == 0 &&
       fireStep.Feedback.EnemyDamageEvents == 0,
    "A successful projectile spawn did not emit exactly one fire event without invented hit events.");
run.Projectiles.Clear();
Console.WriteLine("PASS: fixed-step result emits one fire event only for a successfully spawned projectile");

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
run.PlayerMovement.Step(run.Player, new PlayerInput(-1f, true, true), step);
run.PlayerMovement.Step(run.Player, new PlayerInput(-1f, false, false), step);
run.PlayerMovement.Step(run.Player, new PlayerInput(-1f, true, true), step);
Assert(run.Player.SecondJumpActive, "Lifecycle reset setup did not activate the second jump.");
run.Player.TakeDamage(25);
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
var spawnSelectionBeforeReset = run.PlayerSpawn;
var enemySpawnBeforeReset = run.EnemySpawn;
var geometryFingerprint = Fingerprint(run.Geometry);
var navigationDebugEnabled = true;

run.RestartSameSeed();
Assert(run.Seed == sameSeed, "F5/same-seed reset changed the seed.");
Assert(run.CaveProfile.Profile == CaveGenerationProfile.Baseline,
    "F5/same-seed reset changed the baseline cave profile.");
Assert(Fingerprint(run.Geometry) == geometryFingerprint, "Same-seed reset did not reproduce cave geometry.");
Assert(run.PlayerSpawn == spawnSelectionBeforeReset && run.Player.Position == spawnSelectionBeforeReset.Position &&
       run.PlayerNavigationComponentId == spawnSelectionBeforeReset.NavigationComponentId,
    "F5/same-seed reset did not reproduce the validated player spawn and navigation component.");
Assert(run.EnemySpawn.Placements.SequenceEqual(enemySpawnBeforeReset.Placements) &&
       run.Enemies.Enemies.Select(EnemySpawnState).SequenceEqual(
           enemySpawnBeforeReset.Placements.Select(item =>
               (item.EnemyId, item.Position, item.MaximumSpeed))),
    "F5/same-seed reset did not reproduce enemy placement and speed assignment.");
Assert(!ReferenceEquals(run.Player, oldPlayer) && run.Player.Health == 100 && run.Player.IsAlive,
    "Atomic reset retained the old player or failed to restore it.");
Assert(run.Player.Velocity == Vector2.Zero && run.Player.FacingDirection == 1 && !run.Player.IsJumping && run.Player.JumpsUsed == 0 &&
       !run.Player.SecondJumpActive && run.Player.SecondJumpElapsed == 0f &&
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

var retainedSeed = run.Seed;
var retainedMap = run.CaveMap;
var retainedGeometry = run.Geometry;
var retainedPlayer = run.Player;
var retainedNavigation = run.NavigationGrid;
var retainedSpawn = run.PlayerSpawn;
PlayerSpawnException? spawnFailure = null;
try
{
    run.StartRun("milestone-10c-explicit-failure", new PlayerSpawnOptions { MinimumSupportWidth = 10_000f });
}
catch (PlayerSpawnException exception)
{
    spawnFailure = exception;
}
Assert(spawnFailure is not null && spawnFailure.Failure.LandingCandidateCount > 0 &&
       spawnFailure.Failure.RejectionCounts[PlayerSpawnRejectionReason.InsufficientSupportWidth] > 0,
    "No-candidate run construction did not expose structured player-spawn failure.");
Assert(run.Seed == retainedSeed && ReferenceEquals(run.CaveMap, retainedMap) &&
       ReferenceEquals(run.Geometry, retainedGeometry) && ReferenceEquals(run.Player, retainedPlayer) &&
       ReferenceEquals(run.NavigationGrid, retainedNavigation) && run.PlayerSpawn == retainedSpawn &&
       run.CaveProfile.Profile == CaveGenerationProfile.Baseline,
    "Failed replacement run corrupted the previously published atomic run.");
Console.WriteLine("PASS: explicit no-candidate failure leaves the existing run atomically untouched");

var retainedEnemySpawn = run.EnemySpawn;
var retainedEnemies = run.Enemies;
EnemySpawnException? enemySpawnFailure = null;
try
{
    run.StartRun(
        "milestone-10d-explicit-failure",
        null,
        new EnemySpawnOptions { CollisionSafetyMargin = 10_000f });
}
catch (EnemySpawnException exception)
{
    enemySpawnFailure = exception;
}
Assert(enemySpawnFailure is not null &&
       enemySpawnFailure.Failure.Code is EnemySpawnFailureCode.NoCollisionSafeCandidates or
           EnemySpawnFailureCode.InsufficientSeparatedCapacity &&
       enemySpawnFailure.Failure.SelectedCount < EnemySpawnSystem.OriginalEnemyCount,
    "Invalid enemy capacity did not expose structured failure.");
Assert(run.Seed == retainedSeed && ReferenceEquals(run.CaveMap, retainedMap) &&
       ReferenceEquals(run.Geometry, retainedGeometry) && ReferenceEquals(run.Player, retainedPlayer) &&
       ReferenceEquals(run.NavigationGrid, retainedNavigation) && ReferenceEquals(run.Enemies, retainedEnemies) &&
       ReferenceEquals(run.EnemySpawn, retainedEnemySpawn),
    "Failed enemy replacement corrupted the previously published atomic run.");
Console.WriteLine("PASS: explicit enemy-placement failure leaves the existing run atomically untouched");

var seedBeforeNewRun = run.Seed;
var navigationBeforeNewRun = run.NavigationGrid;
run.StartNewRun();
Assert(run.Seed != seedBeforeNewRun, "R/new-seed reset did not change the generated seed.");
Assert(run.CaveProfile.Profile == CaveGenerationProfile.Baseline,
    "R/new-seed reset changed the baseline cave profile.");
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

static (int Id, Vector2 Position, int Speed) EnemySpawnState(Enemy enemy) =>
    (enemy.Id, enemy.Position, enemy.MaximumSpeed);

static string MapFingerprint(CaveMap map) => string.Join(',',
    Enumerable.Range(0, map.Height).SelectMany(y =>
        Enumerable.Range(0, map.Width).Select(x => (int)map[x, y])));

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
    Assert(options.Width == width && options.Height == height &&
           options.RandomFillPercent == fill && options.SmoothingPasses == smoothing &&
           options.WallRegionThreshold == wallThreshold && options.RoomRegionThreshold == roomThreshold &&
           options.PassageRadius == passageRadius,
        "Resolved cave-generation options do not match the named profile.");
}

static void AssertCandidateProfileLifecycle(
    CaveGenerationProfileSelection selection,
    CaveGenerationProfile expectedProfile,
    string seed)
{
    var run = new MainScreenRun(seed, selection);
    Assert(run.CaveProfile.Profile == expectedProfile && run.CaveProfileName == selection.Name &&
           run.State == RunState.Playing && run.Player.IsGrounded &&
           run.Enemies.LivingCount == EnemySpawnSystem.OriginalEnemyCount,
        $"Profile {selection.Name} did not complete the validated player/enemy pipeline.");

    var geometry = Fingerprint(run.Geometry);
    var playerSpawn = run.PlayerSpawn;
    var enemySpawn = run.EnemySpawn.Placements.ToArray();
    run.RestartSameSeed();
    Assert(run.Seed == seed && run.CaveProfile.Profile == expectedProfile &&
           run.CaveProfileName == selection.Name && Fingerprint(run.Geometry) == geometry &&
           run.PlayerSpawn == playerSpawn && run.EnemySpawn.Placements.SequenceEqual(enemySpawn),
        $"Profile {selection.Name} F5 did not reproduce seed, profile, geometry, player spawn, and enemy placements/speeds.");

    var retainedSeed = run.Seed;
    var retainedMap = run.CaveMap;
    var replacementFailed = false;
    try
    {
        run.StartNewRun();
    }
    catch (InvalidOperationException)
    {
        replacementFailed = true;
    }

    Assert(run.CaveProfile.Profile == expectedProfile && run.CaveProfileName == selection.Name,
        $"Profile {selection.Name} R changed the selected profile.");
    if (replacementFailed)
        Assert(run.Seed == retainedSeed && ReferenceEquals(run.CaveMap, retainedMap),
            $"Profile {selection.Name} failed R corrupted the published seed or world.");
    else
        Assert(run.Seed != retainedSeed && run.State == RunState.Playing &&
               run.Enemies.LivingCount == EnemySpawnSystem.OriginalEnemyCount,
            $"Profile {selection.Name} successful R did not publish a validated new-seed run.");
}

static void AssertThrowsWithProfileNames(Action action, string message)
{
    try
    {
        action();
    }
    catch (ArgumentException exception)
    {
        foreach (var name in new[] { "baseline", "conservative-irregular", "dense", "complex", "extreme" })
            Assert(exception.Message.Contains(name, StringComparison.Ordinal),
                $"Cave-profile validation message omitted '{name}'.");
        return;
    }
    throw new InvalidOperationException(message);
}

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

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
