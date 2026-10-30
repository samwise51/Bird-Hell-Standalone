using System.Numerics;
using System.Reflection;
using BirdHell.Core;
using BirdHell.Entities;
using BirdHell.Presentation;

const float tolerance = 0.00001f;

var camera = new Camera2D();
Assert(camera.Position == Vector2.Zero, "Camera did not start at (0,0).");
Assert(Camera2D.TargetOffset == new Vector2(0f, 1f), "Camera target offset is not exactly (0,1).");
Assert(Camera2D.FixedStepInterpolation == 0.15f, "Camera interpolation factor is not exactly 0.15 per fixed step.");
Assert(Camera2D.VerticalViewSpan == 10f, "Camera vertical span is not exactly 10.");
Console.WriteLine("PASS: camera constants and fresh-launch state match Unity MainScreen");

var target = new Vector2(20f, 9f);
camera.Step(target);
var expectedFirstStep = Vector2.Lerp(Vector2.Zero, target + Camera2D.TargetOffset, 0.15f);
AssertVectorNear(camera.Position, expectedFirstStep, "One camera fixed step did not produce the exact Lerp result.");
Assert(camera.Position.X > 0f, "Camera did not follow player X.");
Assert(camera.Position.Y > 0f, "Camera did not follow player Y.");

var previousDistance = Vector2.Distance(camera.Position, target + Camera2D.TargetOffset);
for (var index = 0; index < 100; index++) camera.Step(target);
var convergedDistance = Vector2.Distance(camera.Position, target + Camera2D.TargetOffset);
Assert(convergedDistance < previousDistance && convergedDistance < 0.001f,
    "Repeated fixed steps did not converge toward the target.");
Console.WriteLine("PASS: camera follows X/Y and converges with exact per-step Lerp semantics");

var unboundedCamera = new Camera2D();
var extremeTarget = new Vector2(100_000f, -100_000f);
unboundedCamera.Step(extremeTarget);
AssertVectorNear(unboundedCamera.Position, Vector2.Lerp(Vector2.Zero, extremeTarget + Camera2D.TargetOffset, 0.15f),
    "Camera position was clamped on X or Y.");
var tinyTargetCamera = new Camera2D();
tinyTargetCamera.Step(new Vector2(0.0001f, -0.9999f));
Assert(tinyTargetCamera.Position != Vector2.Zero, "Camera applied a dead zone to a tiny target displacement.");
var stationaryTargetCamera = new Camera2D();
stationaryTargetCamera.Step(new Vector2(3f, 4f));
AssertVectorNear(stationaryTargetCamera.Position, new Vector2(0.45f, 0.75f),
    "Camera added look-ahead or another target adjustment.");
Console.WriteLine("PASS: camera has no clamps, dead zone, or look-ahead");

var impactCamera = new Camera2D();
impactCamera.Step(new Vector2(3f, 4f));
var impactBaseline = impactCamera.Position;
impactCamera.TriggerImpact(Vector2.UnitX);
Assert(impactCamera.IsShaking && impactCamera.ShakeOffset != Vector2.Zero &&
       impactCamera.Position == impactBaseline,
    "Impact shake did not begin as a rendering-only displacement.");
var shakenView = impactCamera.GetView(1280, 720);
Assert(shakenView.Center == impactBaseline + impactCamera.ShakeOffset,
    "Camera view did not include the active impact displacement.");
for (var index = 0; index < 5; index++) impactCamera.AdvanceImpact(0.02f);
Assert(!impactCamera.IsShaking && impactCamera.ShakeOffset == Vector2.Zero &&
       impactCamera.Position == impactBaseline && impactCamera.GetView(1280, 720).Center == impactBaseline,
    "Impact shake did not expire exactly back at the logical camera baseline.");
impactCamera.TriggerImpact(Vector2.UnitX);
impactCamera.TriggerImpact(-Vector2.UnitX);
Assert(impactCamera.ShakeOffset.Length() <= Camera2D.ImpactShakeAmplitude + tolerance,
    "Repeated impacts accumulated unbounded camera displacement.");
Console.WriteLine("PASS: accepted-impact camera shake is subtle, rendering-only, bounded, and self-clearing");

var fourByThree = camera.GetView(800, 600);
Assert(fourByThree.VerticalSpan == 10f, "4:3 view changed the vertical span.");
AssertNear(fourByThree.HorizontalSpan, 13.333333f, "4:3 horizontal span is incorrect.");
var sixteenByNine = camera.GetView(1920, 1080);
Assert(sixteenByNine.VerticalSpan == 10f, "16:9 view changed the vertical span.");
AssertNear(sixteenByNine.HorizontalSpan, 17.777778f, "16:9 horizontal span is incorrect.");
AssertVectorNear(fourByThree.Center, sixteenByNine.Center, "Viewport resize changed camera position.");
Assert(fourByThree.HorizontalSpan != sixteenByNine.HorizontalSpan,
    "Projection did not update when viewport aspect changed.");
Console.WriteLine("PASS: projection keeps a 10-unit vertical span and responds to viewport aspect");

var run = new MainScreenRun("milestone-7-reset-check");
var resetCamera = new Camera2D();
for (var index = 0; index < 8; index++) resetCamera.Step(run.Player.Position);
var beforeSameSeedReset = resetCamera.Position;
var oldPlayer = run.Player;
var oldGeometry = run.Geometry;
var oldNavigation = run.NavigationGrid;
var oldEnemies = run.Enemies;
var oldProjectiles = run.Projectiles;
var seed = run.Seed;

run.RestartSameSeed();
Assert(run.Seed == seed, "Same-seed reset changed the seed.");
Assert(resetCamera.Position == beforeSameSeedReset, "Same-seed reset snapped or otherwise changed the camera.");
Assert(!ReferenceEquals(run.Player, oldPlayer) && !ReferenceEquals(run.Geometry, oldGeometry) &&
       !ReferenceEquals(run.NavigationGrid, oldNavigation) && !ReferenceEquals(run.Enemies, oldEnemies) &&
       !ReferenceEquals(run.Projectiles, oldProjectiles),
    "Same-seed lifecycle reset was not an atomic replacement of gameplay state.");
run.Player.Position += new Vector2(11f, 7f);
var expectedAfterReplacement = Vector2.Lerp(
    beforeSameSeedReset,
    run.Player.Position + Camera2D.TargetOffset,
    Camera2D.FixedStepInterpolation);
resetCamera.Step(run.Player.Position);
AssertVectorNear(resetCamera.Position, expectedAfterReplacement,
    "Camera did not smooth toward the replacement player after same-seed reset.");
Console.WriteLine("PASS: same-seed reset remains atomic and preserves/smooths the camera");

var beforeNewSeedReset = resetCamera.Position;
var seedBeforeNewRun = run.Seed;
run.StartNewRun();
Assert(run.Seed != seedBeforeNewRun, "New-seed reset did not change the seed.");
Assert(resetCamera.Position == beforeNewSeedReset, "New-seed reset snapped or otherwise changed the camera.");
var expectedAfterNewRun = Vector2.Lerp(
    beforeNewSeedReset,
    run.Player.Position + Camera2D.TargetOffset,
    Camera2D.FixedStepInterpolation);
resetCamera.Step(run.Player.Position);
AssertVectorNear(resetCamera.Position, expectedAfterNewRun,
    "Camera did not smooth toward the replacement player after new-seed reset.");
Console.WriteLine("PASS: new-seed reset preserves the same camera semantics");

var cameraFields = typeof(Camera2D).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
Assert(cameraFields.All(field => !typeof(Player).IsAssignableFrom(field.FieldType)),
    "Camera state retained a Player/entity reference.");
Console.WriteLine("PASS: logical camera contains no stale entity reference");

var deadCamera = new Camera2D();
run.Player.TakeDamage(run.Player.MaximumHealth);
deadCamera.Step(run.Player.Position);
var afterDeathStep = deadCamera.Position;
deadCamera.Step(run.Player.Position);
Assert(deadCamera.Position != afterDeathStep, "Camera stopped following after PlayerDead.");
Console.WriteLine("PASS: PlayerDead does not alter camera behavior");

var preferences = new DebugPresentationSettings();
preferences.ToggleOutlines();
preferences.ToggleCollision();
preferences.ToggleNavigation();
run.RestartSameSeed();
Assert(preferences.ShowOutlines && preferences.ShowCollision && preferences.ShowNavigation,
    "Debug toggle preferences did not survive gameplay reset.");
Assert(resetCamera.Position != Vector2.Zero, "Camera unexpectedly reset while checking presentation preferences.");
Console.WriteLine("PASS: debug presentation preferences survive lifecycle reset");
Console.WriteLine("All Milestone 7 camera/presentation checks passed.");

static void AssertNear(float actual, float expected, string message)
{
    if (MathF.Abs(actual - expected) > tolerance) throw new InvalidOperationException($"{message} Expected {expected:R}, got {actual:R}.");
}

static void AssertVectorNear(Vector2 actual, Vector2 expected, string message)
{
    if (Vector2.Distance(actual, expected) > tolerance)
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
