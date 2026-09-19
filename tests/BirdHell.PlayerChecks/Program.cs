using System.Numerics;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;

const float step = 0.02f;
var collider = PlayerCollider.Original;

var generatedGeometry = new CaveGeometryBuilder().Build(new CaveGenerator().Generate("milestone-3-safe-spawn"));
var generatedCollision = new TerrainCollisionSystem(generatedGeometry);
var firstSpawn = PlayerSpawnSystem.FindSafeSpawn(generatedGeometry, collider);
var secondSpawn = PlayerSpawnSystem.FindSafeSpawn(generatedGeometry, collider);
Assert(firstSpawn == secondSpawn, "Same cave did not produce the same spawn.");
Assert(!generatedCollision.Intersects(firstSpawn, collider), "Safe spawn intersects terrain.");
Console.WriteLine("PASS: safe spawn is deterministic and does not intersect generated terrain");

var airborne = new Player(new Vector2(0f, 5f), collider);
var emptyMovement = CreateMovement([]);
var airborneStart = airborne.Position.Y;
emptyMovement.Step(airborne, new PlayerInput(0f, false, true), step);
Assert(airborne.Position.Y < airborneStart && airborne.Velocity.Y < 0f, "Gravity did not move airborne player downward.");
Console.WriteLine("PASS: gravity moves an airborne player downward");

var floorSegments = new[] { new TerrainSegment(new Vector2(-20f, 0f), new Vector2(20f, 0f)) };
var floorCollision = new TerrainCollisionSystem(floorSegments);
var floorMovement = new PlayerMovementSystem(floorCollision);
var falling = new Player(new Vector2(0f, 4f), collider);
for (var i = 0; i < 200; i++) floorMovement.Step(falling, default, step);
Assert(falling.IsGrounded, "Player did not become grounded on floor.");
Assert(MathF.Abs((falling.Position.Y + collider.BottomOffset) - TerrainCollisionSystem.Skin) < 0.05f, "Player did not settle on floor.");
Console.WriteLine("PASS: player settles on a floor and becomes grounded");

floorMovement.Step(falling, new PlayerInput(0f, true, true), step);
Assert(!falling.IsGrounded && falling.Velocity.Y > 0f && falling.JumpsUsed == 1, "Grounded jump did not launch player.");
floorMovement.Step(falling, new PlayerInput(0f, true, true), step);
Assert(falling.JumpsUsed == 2, "Original second jump was not available.");
var velocityBeforeThirdJump = falling.Velocity.Y;
floorMovement.Step(falling, new PlayerInput(0f, true, true), step);
Assert(falling.JumpsUsed == 2 && falling.Velocity.Y < velocityBeforeThirdJump, "Player jumped more than the original two-jump limit.");
Console.WriteLine("PASS: grounded jump and original two-jump limit work");

var wallSegments = new[]
{
    new TerrainSegment(new Vector2(-20f, 0f), new Vector2(20f, 0f)),
    new TerrainSegment(new Vector2(2f, 0f), new Vector2(2f, 8f)),
};
var wallCollision = new TerrainCollisionSystem(wallSegments);
var wallMovement = new PlayerMovementSystem(wallCollision);
var runner = StandingPlayer(0f, 0f, collider);
for (var i = 0; i < 100; i++) wallMovement.Step(runner, new PlayerInput(1f, false, false), step);
Assert(runner.ColliderCenter.X + collider.Radius <= 2f + 0.01f, "Player passed through vertical wall.");
Console.WriteLine("PASS: player does not pass through a vertical wall");

var roomSegments = new[]
{
    new TerrainSegment(new Vector2(-20f, 0f), new Vector2(20f, 0f)),
    new TerrainSegment(new Vector2(-20f, 3f), new Vector2(20f, 3f)),
};
var roomCollision = new TerrainCollisionSystem(roomSegments);
var roomMovement = new PlayerMovementSystem(roomCollision);
var jumper = StandingPlayer(0f, 0f, collider);
var maximumTop = float.NegativeInfinity;
for (var i = 0; i < 120; i++)
{
    roomMovement.Step(jumper, new PlayerInput(0f, i == 0, true), step);
    maximumTop = MathF.Max(maximumTop, jumper.ColliderCenter.Y + collider.Height / 2f);
}
Assert(maximumTop <= 3f + 0.015f, "Player passed through ceiling.");
Console.WriteLine("PASS: player does not pass through a ceiling while jumping");

var slope = new TerrainSegment(new Vector2(-5f, 0f), new Vector2(5f, 5f));
var slopeCollision = new TerrainCollisionSystem([slope]);
var slopeMovement = new PlayerMovementSystem(slopeCollision);
var slopeFloorY = 2.5f;
var climber = StandingPlayer(0f, slopeFloorY, collider);
slopeCollision.RefreshContacts(climber);
Assert(climber.IsGrounded && climber.GroundNormal.Y >= MathF.Cos(MathF.PI / 4f) - 0.01f, "45-degree slope was not walkable.");
var slopeStart = climber.Position;
for (var i = 0; i < 25; i++) slopeMovement.Step(climber, new PlayerInput(1f, false, false), step);
Assert(climber.Position.X > slopeStart.X && climber.Position.Y > slopeStart.Y, "Player did not follow walkable slope.");
Assert(!slopeCollision.Intersects(climber.Position, collider, 0.01f), "Player penetrated diagonal slope.");
Console.WriteLine("PASS: 45-degree diagonal contact is grounded and slope-following is consistent");

var coarse = SimulateAirborne(0.02f, 25);
var fine = SimulateAirborne(0.01f, 50);
Assert(Vector2.Distance(coarse.Position, fine.Position) < 0.12f, "Simulation changed materially with timestep.");
Assert(Vector2.Distance(coarse.Velocity, fine.Velocity) < 0.02f, "Velocity changed materially with timestep.");
Console.WriteLine("PASS: fixed-step simulation is reasonably timestep-independent");

static Player StandingPlayer(float x, float floorY, PlayerCollider collider) =>
    new(new Vector2(x - collider.Offset.X, floorY - collider.BottomOffset + TerrainCollisionSystem.Skin), collider);

static Player SimulateAirborne(float deltaTime, int count)
{
    var player = new Player(new Vector2(0f, 10f));
    var movement = CreateMovement([]);
    for (var i = 0; i < count; i++) movement.Step(player, new PlayerInput(1f, false, true), deltaTime);
    return player;
}

static PlayerMovementSystem CreateMovement(IEnumerable<TerrainSegment> segments) =>
    new(new TerrainCollisionSystem(segments));

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
