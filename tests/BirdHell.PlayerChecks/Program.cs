using System.Numerics;
using BirdHell.Entities;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Analysis;
using BirdHell.World.Geometry;

const float step = 0.02f;
var collider = PlayerCollider.Original;

var firstSpawnWorld = BuildSpawnWorld(new CaveGenerator().Generate("milestone-3-safe-spawn"));
var secondSpawnWorld = BuildSpawnWorld(new CaveGenerator().Generate("milestone-3-safe-spawn"));
var firstSpawn = firstSpawnWorld.Result.Selection!;
var secondSpawn = secondSpawnWorld.Result.Selection!;
Assert(firstSpawnWorld.Result.IsSuccess && secondSpawnWorld.Result.IsSuccess && firstSpawn == secondSpawn,
    "Same cave did not produce the same spawn selection.");
Assert(!firstSpawnWorld.Collision.Intersects(firstSpawn.Position, collider),
    "Selected player capsule intersects terrain.");
var groundedSpawn = new Player(firstSpawn.Position, collider);
firstSpawnWorld.Collision.RefreshContacts(groundedSpawn);
Assert(groundedSpawn.IsGrounded && groundedSpawn.GroundNormal.Y >= TerrainCollisionSystem.MinimumGroundNormalY,
    "Selected player spawn is not grounded on a walkable surface.");
Assert(firstSpawn.SupportWidth >= 1f && firstSpawn.Headroom >= TerrainCollisionSystem.Skin,
    "Selected player spawn lacks required support or headroom.");
Assert(firstSpawn.NavigationComponentId == firstSpawnWorld.Navigation.GetComponentId(firstSpawn.NavigationCell) &&
       firstSpawn.LogicalComponentId == firstSpawnWorld.Report.MainLogicalComponent?.Id,
    "Selected player spawn does not belong to its reported navigation and main logical components.");
Console.WriteLine("PASS: topology-aware spawn is deterministic, clear, grounded, supported, and component-verified");

var blocked = BuildSpawnWorld(CreateNarrowCorridorMap());
Assert(!blocked.Result.IsSuccess && blocked.Result.Failure is not null,
    "Capsule-blocked low corridor did not return structured spawn failure.");
Console.WriteLine("PASS: ceilings and blocked capsules are rejected with structured failure");

var disconnected = BuildSpawnWorld(CreateDisconnectedSpawnMap());
Assert(disconnected.Result.IsSuccess &&
       disconnected.Result.Selection!.LogicalComponentId == disconnected.Report.MainLogicalComponent?.Id &&
       disconnected.Result.Selection.NavigationComponentCellCount >= new PlayerSpawnOptions().MinimumNavigationComponentCells,
    "Disconnected/tiny navigation components were not handled safely.");
Console.WriteLine("PASS: disconnected and tiny components do not displace the verified main-component spawn");

var membershipMap = CreateSmallPocketMap();
var membershipGeometry = new CaveGeometryBuilder().Build(membershipMap);
var membershipOccupancy = new TerrainOccupancy(membershipGeometry);
var validMembershipNavigation = new NavigationGrid(membershipOccupancy);
var unrelatedAnalysisNavigation = new NavigationGrid(membershipOccupancy, center: new Vector2(500f, 500f));
var mismatchedReport = new CaveTopologyAnalyzer().Analyze(
    membershipMap, membershipGeometry, membershipOccupancy, unrelatedAnalysisNavigation);
var mismatchedMembership = PlayerSpawnSystem.Select(
    mismatchedReport,
    membershipGeometry,
    membershipOccupancy,
    new TerrainCollisionSystem(membershipOccupancy),
    validMembershipNavigation,
    collider,
    new PlayerSpawnOptions
    {
        MinimumNavigationComponentCells = 1,
        MinimumEnemyClearNavigationCells = 0,
        MinimumLocalNavigationCells = 1,
    });
Assert(!mismatchedMembership.IsSuccess &&
       mismatchedMembership.Failure!.RejectionCounts[PlayerSpawnRejectionReason.MissingNavigationComponent] > 0,
    "Selector guessed navigation membership for candidates that were not represented in the analyzed grid.");
Console.WriteLine("PASS: candidate navigation membership is verified rather than guessed");

var trapped = BuildSpawnWorld(CreateSmallPocketMap(), new PlayerSpawnOptions
{
    MinimumNavigationComponentCells = 1,
    MinimumEnemyClearNavigationCells = 0,
    MinimumLocalNavigationCells = 100,
});
Assert(!trapped.Result.IsSuccess &&
       trapped.Result.Failure!.RejectionCounts[PlayerSpawnRejectionReason.InsufficientLocalMovementSpace] > 0,
    "Trapped pocket was not rejected for insufficient local movement space.");
Console.WriteLine("PASS: trapped pockets are rejected by local escape-area validation");

var unsupported = BuildSpawnWorld(CreateSmallPocketMap(), new PlayerSpawnOptions
{
    MinimumSupportWidth = 100f,
    MinimumNavigationComponentCells = 1,
    MinimumEnemyClearNavigationCells = 0,
    MinimumLocalNavigationCells = 1,
});
Assert(!unsupported.Result.IsSuccess &&
       unsupported.Result.Failure!.RejectionCounts[PlayerSpawnRejectionReason.InsufficientSupportWidth] > 0,
    "Narrow support did not fail the support-width filter.");
Console.WriteLine("PASS: narrow pins and inadequate continuous support are rejected");

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

var firstJumpResult = floorMovement.Step(falling, new PlayerInput(0f, true, true), step);
Assert(!falling.IsGrounded && falling.Velocity.Y > 0f && falling.JumpsUsed == 1 &&
       firstJumpResult.JumpPerformed && firstJumpResult.JumpNumber == 1 && !firstJumpResult.SecondJumpPerformed,
    "Grounded jump did not launch player or report jump one.");
var firstJumpVelocity = falling.Velocity.Y;
var secondJumpResult = floorMovement.Step(falling, new PlayerInput(0f, true, true), step);
Assert(falling.JumpsUsed == 2 && secondJumpResult.SecondJumpPerformed &&
       MathF.Abs(falling.Velocity.Y - firstJumpVelocity) < 0.00001f,
    "Original second jump availability, accepted event, or unchanged jump physics failed.");
var velocityBeforeThirdJump = falling.Velocity.Y;
var failedThirdJumpResult = floorMovement.Step(falling, new PlayerInput(0f, true, true), step);
Assert(falling.JumpsUsed == 2 && falling.Velocity.Y < velocityBeforeThirdJump && !failedThirdJumpResult.JumpPerformed,
    "Player jumped more than the original two-jump limit or reported a rejected jump as accepted.");
var walkedOffResult = emptyMovement.Step(new Player(new Vector2(0f, 5f), collider), default, step);
Assert(!walkedOffResult.JumpPerformed && !walkedOffResult.SecondJumpPerformed,
    "Ordinary airborne/falling movement emitted a jump event.");
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

var steepSegment = new TerrainSegment(new Vector2(-2f, 0f), new Vector2(2f, 6.928203f));
var steepCollision = new TerrainCollisionSystem([steepSegment]);
var steepEdge = Vector2.Normalize(steepSegment.B - steepSegment.A);
var steepNormal = new Vector2(-steepEdge.Y, steepEdge.X);
var steepSurfacePoint = (steepSegment.A + steepSegment.B) / 2f;
var steepCenter = steepSurfacePoint + steepNormal * (collider.Radius + TerrainCollisionSystem.Skin);
var steepPlayer = new Player(steepCenter - collider.Offset, collider);
steepCollision.RefreshContacts(steepPlayer);
Assert(!steepPlayer.IsGrounded, "A slope steeper than the 45-degree maximum was accepted as ground.");
Console.WriteLine("PASS: slopes steeper than 45 degrees are rejected as grounded spawn support");

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

static (PlayerSpawnResult Result, CaveTopologyReport Report, NavigationGrid Navigation,
    TerrainCollisionSystem Collision) BuildSpawnWorld(CaveMap map, PlayerSpawnOptions? options = null)
{
    var geometry = new CaveGeometryBuilder().Build(map);
    var occupancy = new TerrainOccupancy(geometry);
    var collision = new TerrainCollisionSystem(occupancy);
    var navigation = new NavigationGrid(occupancy);
    var report = new CaveTopologyAnalyzer().Analyze(map, geometry, occupancy, navigation);
    var result = PlayerSpawnSystem.Select(
        report, geometry, occupancy, collision, navigation, PlayerCollider.Original, options);
    return (result, report, navigation, collision);
}

static CaveMap CreateNarrowCorridorMap()
{
    var tiles = WallTiles(17, 9);
    OpenRectangle(tiles, 1, 15, 4, 4);
    return new CaveMap(tiles);
}

static CaveMap CreateDisconnectedSpawnMap()
{
    var tiles = WallTiles(25, 15);
    OpenRectangle(tiles, 1, 18, 1, 13);
    OpenRectangle(tiles, 22, 23, 6, 8);
    return new CaveMap(tiles);
}

static CaveMap CreateSmallPocketMap()
{
    var tiles = WallTiles(9, 9);
    OpenRectangle(tiles, 2, 6, 2, 6);
    return new CaveMap(tiles);
}

static CaveTile[,] WallTiles(int width, int height)
{
    var tiles = new CaveTile[width, height];
    for (var x = 0; x < width; x++)
    for (var y = 0; y < height; y++)
        tiles[x, y] = CaveTile.Wall;
    return tiles;
}

static void OpenRectangle(CaveTile[,] tiles, int minX, int maxX, int minY, int maxY)
{
    for (var x = minX; x <= maxX; x++)
    for (var y = minY; y <= maxY; y++)
        tiles[x, y] = CaveTile.Open;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
