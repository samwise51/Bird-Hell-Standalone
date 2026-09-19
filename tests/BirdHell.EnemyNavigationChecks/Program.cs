using System.Numerics;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;

const float step = 0.02f;
const float tolerance = 0.0001f;

var defaultGrid = new NavigationGrid([]);
Assert(defaultGrid.Width == 128 && defaultGrid.Height == 72 && defaultGrid.NodeSize == 1f, "Navigation dimensions differ from the GridGraph.");
Assert(defaultGrid.Minimum == new Vector2(-64f, -36f), "Navigation grid is not centered at the world origin.");
Assert(defaultGrid.CellToWorld(new NavigationCell(0, 0)) == new Vector2(-63.5f, -35.5f), "Navigation cell-center alignment is incorrect.");
Pass("navigation grid dimensions and world alignment");

var outlineGrid = Grid([new TerrainSegment(new Vector2(0f, -2f), new Vector2(0f, 2f))], scanRadius: 0.5f);
Assert(!outlineGrid.IsWalkable(3, 3), "A node touching authored outline geometry was not blocked.");
Assert(outlineGrid.IsWalkable(1, 3), "A distant node was incorrectly blocked.");
Pass("obstacles derive from outline segments with the 0.5 scan radius");

var openGrid = Grid([], 5, 5, 0f);
Assert(openGrid.GetNeighbors(new NavigationCell(2, 2)).Count == 8, "Open-space navigation is not eight-way.");
Pass("eight-way connectivity");

var cornerGrid = Grid([
    new TerrainSegment(new Vector2(0.9f, 0f), new Vector2(1.1f, 0f)),
    new TerrainSegment(new Vector2(0f, 0.9f), new Vector2(0f, 1.1f)),
], 5, 5, 0.15f);
Assert(!cornerGrid.GetNeighbors(new NavigationCell(2, 2)).Contains(new NavigationCell(3, 3)), "Diagonal movement cut a blocked corner.");
Pass("diagonal corner-cut prevention");

var aroundGrid = Grid([new TerrainSegment(new Vector2(0f, -2f), new Vector2(0f, 2f))], 7, 7, 0.15f);
var aroundFinder = new AStarPathfinder(aroundGrid);
var pathOne = aroundFinder.FindPath(new Vector2(-3f, 0f), new Vector2(3f, 0f));
var pathTwo = aroundFinder.FindPath(new Vector2(-3f, 0f), new Vector2(3f, 0f));
Assert(pathOne is { Count: > 0 } && pathTwo is not null && pathOne.SequenceEqual(pathTwo), "Identical A* input was not deterministic.");
Assert(pathOne!.Any(point => MathF.Abs(point.Y) >= 3f), "A* did not route around the authored obstacle.");
Pass("deterministic A* and routing around an authored obstacle");

var dividedGrid = Grid([new TerrainSegment(new Vector2(0f, -4f), new Vector2(0f, 4f))], 7, 7, 0.15f);
Assert(new AStarPathfinder(dividedGrid).FindPath(new Vector2(-3f, 0f), new Vector2(3f, 0f)) is null,
    "A* returned a partial path for an unreachable target.");
Pass("unreachable targets return no partial path");

var geometry = new CaveGeometryBuilder().Build(new CaveGenerator().Generate("milestone-5-checks"));
var generatedTerrain = new TerrainCollisionSystem(geometry);
var generatedGrid = new NavigationGrid(geometry);
var spawned = EnemySpawnSystem.Spawn(geometry, generatedGrid, generatedTerrain, "milestone-5-checks");
Assert(spawned.Count == 15 && spawned.All(enemy => enemy.MaximumSpeed is >= 2 and <= 9), "Enemy count/speed range is incorrect.");
Assert(spawned.All(enemy => enemy.MaximumSpeed == (int)enemy.MaximumSpeed), "Enemy speeds are not integral.");
Pass("15 enemies receive independent integer speeds in range 2-9");

var emptyTerrain = new TerrainCollisionSystem([]);
var movementGrid = new NavigationGrid([], 16, 16);
var navigation = new EnemyNavigationSystem(new AStarPathfinder(movementGrid), emptyTerrain);
var movingEnemy = AtCenter(0, new Vector2(-3.5f, 0.5f), 9);
navigation.Step([movingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(MathF.Abs(movingEnemy.Velocity.Length() - 0.2f) < tolerance, "Enemy acceleration did not use 10 units/sec².");
for (var index = 0; index < 200; index++) navigation.Step([movingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(movingEnemy.Velocity.Length() <= movingEnemy.MaximumSpeed + tolerance, "Enemy exceeded its randomized speed limit.");
Pass("enemy acceleration and maximum speed limiting");

var repathingEnemy = AtCenter(0, new Vector2(-3.5f, 0.5f), 5);
navigation.Step([repathingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(repathingEnemy.SuccessfulRepathCount == 1, "Enemy did not calculate its initial path.");
for (var index = 0; index < 24; index++) navigation.Step([repathingEnemy], new Vector2(3.5f, 0.5f), step);
Assert(repathingEnemy.SuccessfulRepathCount == 2, "Enemy did not replan on the 0.5-second interval.");
Pass("0.5-second repath interval");

var wallTerrain = new TerrainCollisionSystem([new TerrainSegment(new Vector2(0f, -5f), new Vector2(0f, 5f))]);
var blockedNavigation = new EnemyNavigationSystem(new AStarPathfinder(dividedGrid), wallTerrain);
var wallEnemy = AtCenter(0, new Vector2(-1f, 0f), 9);
wallEnemy.Velocity = new Vector2(9f, 0f);
for (var index = 0; index < 30; index++) blockedNavigation.Step([wallEnemy], new Vector2(3f, 0f), step);
Assert(wallEnemy.ColliderCenter.X <= -wallEnemy.ColliderRadius + 0.01f, "Flying enemy crossed terrain geometry.");
Pass("enemy circle terrain collision");

var separationNavigation = new EnemyNavigationSystem(new AStarPathfinder(openGrid), emptyTerrain);
var clusteredA = AtCenter(0, Vector2.Zero, 2);
var clusteredB = AtCenter(1, Vector2.Zero, 2);
separationNavigation.SeparateEnemies([clusteredA, clusteredB]);
var separatedState = (clusteredA.Position, clusteredB.Position);
var clusteredC = AtCenter(0, Vector2.Zero, 2);
var clusteredD = AtCenter(1, Vector2.Zero, 2);
separationNavigation.SeparateEnemies([clusteredC, clusteredD]);
Assert(separatedState == (clusteredC.Position, clusteredD.Position), "Enemy separation was not deterministic.");
Assert(Vector2.Distance(clusteredA.ColliderCenter, clusteredB.ColliderCenter) >= Enemy.OriginalColliderRadius * 2f - 0.001f,
    "Enemy separation left circles permanently coincident.");
Pass("small deterministic enemy circle separation");

var player = new Player(Vector2.Zero);
Assert(player.Health == 100 && player.IsAlive, "Player did not start alive at 100 health.");
Pass("player starts at 100 health");

var contact = new EnemyCombatSystem();
var touching = AtCenter(0, player.ColliderCenter, 2);
for (var index = 0; index < 50; index++) contact.Step([touching], player, step);
Assert(player.Health == 100, "Contact damage fired before the original >1-second threshold.");
contact.Step([touching], player, step);
Assert(player.Health == 75 && touching.ContactTimer == 0f, "Contact did not deal 25 and reset only its timer after >1 second.");
Pass("contact damage and original >1-second timer rule");

var playerForTimers = new Player(Vector2.Zero);
var timerA = AtCenter(0, playerForTimers.ColliderCenter, 2);
var timerB = AtCenter(1, new Vector2(20f, 20f), 2);
for (var index = 0; index < 51; index++) contact.Step([timerA, timerB], playerForTimers, step);
Assert(timerA.ContactTimer == 0f && timerB.ContactTimer > 1f, "Enemies did not maintain independent continuously advancing timers.");
timerB.Position = playerForTimers.ColliderCenter - timerB.ColliderOffset;
contact.Step([timerA, timerB], playerForTimers, step);
Assert(playerForTimers.Health == 50, "A separately primed enemy timer did not deal contact damage immediately on overlap.");
Pass("separate continuously advancing contact timers");

var mortal = new Player(Vector2.Zero);
for (var index = 0; index < 4; index++) mortal.TakeDamage(25);
Assert(!mortal.IsAlive && mortal.Health == 0, "Player did not die at zero health.");
mortal.Reset(new Vector2(4f, 5f));
Assert(mortal.IsAlive && mortal.Health == 100 && mortal.Position == new Vector2(4f, 5f), "Player reset did not restore health/state.");
Pass("player death and regeneration reset");

var projectileEnemy = AtCenter(0, new Vector2(1f, 0f), 5);
var projectiles = new ProjectileSystem(emptyTerrain);
for (var hit = 1; hit <= 4; hit++)
{
    projectiles.Spawn(new Projectile(Vector2.Zero, Vector2.UnitX, 20f, 25, 0.1f, 0f, 0f));
    projectiles.Step(0.1f, [new ProjectileTarget(projectileEnemy, projectileEnemy.ColliderCenter, projectileEnemy.ColliderRadius)]);
    Assert(projectiles.Projectiles.Count == 0, "Projectile was not consumed on enemy impact.");
    if (hit < 4) Assert(projectileEnemy.IsAlive && projectileEnemy.Health == 100 - hit * 25, "Enemy did not survive one of the first three hits.");
}
Assert(!projectileEnemy.IsAlive && projectileEnemy.Health == 0, "Fourth default bullet did not kill the enemy.");
Pass("projectile damage, first-three survival, and fourth-hit death");

var deadTimer = projectileEnemy.ContactTimer;
var deadPosition = projectileEnemy.Position;
blockedNavigation.Step([projectileEnemy], Vector2.Zero, step);
contact.Step([projectileEnemy], new Player(Vector2.Zero), step);
projectileEnemy.TakeDamage(25);
Assert(projectileEnemy.Position == deadPosition && projectileEnemy.ContactTimer == deadTimer && projectileEnemy.Health == 0,
    "Dead enemy still received damage/navigation/contact updates.");
Pass("dead enemies leave active gameplay systems");

var population = new EnemyPopulation();
population.Reset([AtCenter(0, Vector2.Zero, 2), AtCenter(1, Vector2.One, 3)]);
Assert(!population.AllEnemiesDead, "Population started in all-dead state.");
foreach (var enemy in population.Enemies) enemy.TakeDamage(100);
Assert(population.AllEnemiesDead && population.CreateProjectileTargets().Count == 0, "Final enemy death did not expose AllEnemiesDead/remove targets.");
Pass("AllEnemiesDead after final death");

population.Reset(EnemySpawnSystem.Spawn(geometry, generatedGrid, generatedTerrain, "milestone-5-checks"));
Assert(population.LivingCount == 15 && !population.AllEnemiesDead, "Regeneration did not restore 15 living enemies.");
var equivalent = EnemySpawnSystem.Spawn(geometry, generatedGrid, generatedTerrain, "milestone-5-checks");
Assert(population.Enemies.Select(State).SequenceEqual(equivalent.Select(State)), "Same deterministic setup produced different enemy state.");
var equivalentPath = new AStarPathfinder(generatedGrid).FindPath(population.Enemies[0].ColliderCenter, Vector2.Zero);
var repeatedPath = new AStarPathfinder(generatedGrid).FindPath(equivalent[0].ColliderCenter, Vector2.Zero);
Assert((equivalentPath is null && repeatedPath is null) || equivalentPath is not null && repeatedPath is not null && equivalentPath.SequenceEqual(repeatedPath),
    "Same deterministic setup produced different navigation state.");
Pass("regeneration count and equivalent deterministic enemy/navigation setup");

static NavigationGrid Grid(IEnumerable<TerrainSegment> segments, int width = 7, int height = 7, float scanRadius = 0.15f) =>
    new(segments, width, height, 1f, Vector2.Zero, scanRadius);

static Enemy AtCenter(int id, Vector2 center, int speed) => new(id, center - Enemy.OriginalColliderOffset, speed);

static (Vector2 Position, int Speed, int Health, bool Alive) State(Enemy enemy) =>
    (enemy.Position, enemy.MaximumSpeed, enemy.Health, enemy.IsAlive);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Pass(string message) => Console.WriteLine($"PASS: {message}");
