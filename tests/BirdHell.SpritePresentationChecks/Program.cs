using System.Numerics;
using System.Reflection;
using BirdHell.Entities;
using BirdHell.Presentation;

const float tolerance = 0.00001f;

AssertVector(BirdHellVisuals.PlayerIdle.Frames[0].Region.WorldSize, new Vector2(2f, 1.48f),
    "Player PPU/root-scale world dimensions are wrong.");
AssertVector(BirdHellVisuals.Enemy.Frames[0].Region.WorldSize, new Vector2(1.2f, 1.23f),
    "Enemy PPU/root-scale world dimensions are wrong.");
Console.WriteLine("PASS: player and enemy dimensions preserve Unity 100 PPU semantics");

CheckLoop(BirdHellVisuals.PlayerIdle, [0, 1, 2, 3], 1f / 4.2f, "idle");
CheckLoop(BirdHellVisuals.PlayerRun, [8, 9, 10, 11], 1f / 12f, "run");
CheckLoop(BirdHellVisuals.PlayerJump, [12, 13, 14, 15], 1f / 12f, "jump");
CheckFrames(BirdHellVisuals.PlayerRoll, [18, 19, 20, 21, 17], 0.1f, "roll");
Assert(BirdHellVisuals.PlayerRoll.Frames.Select(frame => frame.Region.Source).SequenceEqual(new PixelRectangle[]
{
    new(200, 481, 50, 37), new(250, 481, 50, 37), new(300, 481, 50, 37),
    new(0, 444, 50, 37), new(150, 481, 50, 37),
}), "Roll source rectangles do not match the audited atlas slices.");
Assert(!BirdHellVisuals.PlayerRoll.Looping, "Roll clip must play one complete non-looping cycle.");
var rollPlayback = new AnimationPlayback(BirdHellVisuals.PlayerRoll);
rollPlayback.Advance(BirdHellVisuals.PlayerRoll.Duration);
Assert(rollPlayback.IsFinished && rollPlayback.CurrentFrameIndex == 4,
    "Roll clip did not complete on its audited final frame.");
CheckLoop(BirdHellVisuals.PlayerCrouch, [4, 5, 6, 7], 1f / 6f, "crouch");
CheckFrames(BirdHellVisuals.PlayerAttack, [47, 48, 49, 50, 47], 1f / 90f, "attack");
Assert(!BirdHellVisuals.PlayerAttack.Looping, "Attack clip must be non-looping.");
var attackPlayback = new AnimationPlayback(BirdHellVisuals.PlayerAttack);
attackPlayback.Advance(BirdHellVisuals.PlayerAttack.Duration * 2f);
Assert(attackPlayback.IsFinished && attackPlayback.CurrentFrameIndex == 4,
    "Attack did not stop on its final frame.");
Console.WriteLine("PASS: all player frame sequences, effective timings, and non-looping attack semantics");

var player = new PlayerAnimationState();
player.Update(new PlayerVisualSnapshot(true, false, true, 8f), true, false, 0f);
Assert(player.State == PlayerVisualState.Attack, "Attack did not have highest visual priority.");
player.Reset(new PlayerVisualSnapshot(true, false, false, 0f));
Assert(player.State == PlayerVisualState.Jump, "Airborne player did not select Jump.");
player.Reset(new PlayerVisualSnapshot(true, true, true, 0f));
Assert(player.State == PlayerVisualState.Crouch, "Grounded crouching player did not select Crouch.");
player.Reset(new PlayerVisualSnapshot(true, true, false, 0.011f));
Assert(player.State == PlayerVisualState.Run, "Movement above 0.01 did not select Run.");
player.Reset(new PlayerVisualSnapshot(true, true, false, 0.01f));
Assert(player.State == PlayerVisualState.Idle, "Movement at the strict 0.01 threshold did not select Idle.");
Console.WriteLine("PASS: repaired player visual-state priority and original run threshold");

var airborneSnapshot = new PlayerVisualSnapshot(true, false, false, 0f);
player.Reset(airborneSnapshot);
player.Update(airborneSnapshot, false, false, 0f);
Assert(player.State == PlayerVisualState.Jump, "First jump or walking off terrain incorrectly selected Roll.");
player.Update(airborneSnapshot, false, true, 0f);
Assert(player.State == PlayerVisualState.Roll && player.Playback.CurrentFrameIndex == 0,
    "Accepted second jump did not start Roll on its first frame.");
player.Update(airborneSnapshot, false, false, 0.15f);
Assert(player.State == PlayerVisualState.Roll && player.Playback.CurrentFrameIndex == 1,
    "Roll did not progress or restarted merely because the player remained airborne.");
player.Update(airborneSnapshot, false, false, 0.1f);
Assert(player.Playback.CurrentFrameIndex == 2, "Roll frame sequence did not continue without restarting.");
player.Update(airborneSnapshot, true, false, 0f);
Assert(player.State == PlayerVisualState.Attack, "Attack did not retain visual priority over an active Roll.");
player.Update(airborneSnapshot, false, false, BirdHellVisuals.PlayerAttack.Duration);
Assert(player.State == PlayerVisualState.Roll && player.Playback.CurrentFrameIndex > 0,
    "Roll did not resume at its progressed frame after Attack priority ended.");

player.Reset(airborneSnapshot);
player.Update(airborneSnapshot, false, true, 0f);
player.Update(airborneSnapshot, false, false, BirdHellVisuals.PlayerRoll.Duration);
Assert(player.State == PlayerVisualState.Jump, "Completed airborne Roll did not immediately return to Jump.");
player.Update(airborneSnapshot, false, true, 0f);
player.Update(new PlayerVisualSnapshot(true, true, false, 0f), false, false, 0f);
Assert(player.State == PlayerVisualState.Idle, "Landing did not clear active Roll state.");
foreach (var resetName in new[] { "R", "F5" })
{
    player.Reset(airborneSnapshot);
    player.Update(airborneSnapshot, false, true, 0f);
    player.Reset(new PlayerVisualSnapshot(true, true, false, 0f));
    Assert(player.State == PlayerVisualState.Idle && player.Playback.CurrentFrameIndex == 0,
        $"{resetName} reset did not clear Roll state.");
}
Console.WriteLine("PASS: accepted-second-jump Roll trigger, progression, priority, completion, landing, and reset semantics");

Assert(PlayerAnimationState.FlipForFacing(1) == SpriteFlip.None &&
       PlayerAnimationState.FlipForFacing(-1) == SpriteFlip.Horizontal,
    "Player facing did not map to horizontal sprite flip.");
CheckLoop(BirdHellVisuals.Enemy, [0, 1, 2, 3], 1f / 12f, "enemy");
Assert(BirdHellVisuals.EnemyNativeFacing == HorizontalFacing.Left,
    "Presentation metadata does not represent the eagle artwork's native left-facing orientation.");
var facingEnemy = new Enemy(99, Vector2.Zero, 4)
{
    Velocity = new Vector2(2f, 3f),
    Path = new[] { new Vector2(1f, 2f), new Vector2(4f, 5f) },
    PathIndex = 1,
    SteeringTarget = new Vector2(4f, 5f),
};
var preservedVelocity = facingEnemy.Velocity;
var preservedPath = facingEnemy.Path;
var preservedPathIndex = facingEnemy.PathIndex;
var preservedTarget = facingEnemy.SteeringTarget;
facingEnemy.UpdateFacingFromHorizontalMovement(0.02f);
Assert(facingEnemy.FacingDirection == 1 && WorldAnimationState.EnemyFlip(facingEnemy.FacingDirection) == SpriteFlip.Horizontal,
    "Meaningful positive movement did not render the native-left eagle facing right.");
facingEnemy.UpdateFacingFromHorizontalMovement(-0.02f);
Assert(facingEnemy.FacingDirection == -1 && WorldAnimationState.EnemyFlip(facingEnemy.FacingDirection) == SpriteFlip.None,
    "Meaningful negative movement did not render the native-left eagle facing left.");
foreach (var nearZero in new[] { -0.009f, 0f, 0.009f })
{
    facingEnemy.UpdateFacingFromHorizontalMovement(nearZero);
    Assert(facingEnemy.FacingDirection == -1, "Near-zero horizontal movement did not preserve prior facing.");
}
Assert(facingEnemy.Velocity == preservedVelocity && ReferenceEquals(facingEnemy.Path, preservedPath) &&
       facingEnemy.PathIndex == preservedPathIndex && facingEnemy.SteeringTarget == preservedTarget,
    "Facing updates altered navigation velocity/path state.");
Console.WriteLine("PASS: native eagle orientation, movement-facing mapping, dead band, and navigation isolation");

Assert(BirdHellVisuals.BackgroundFilter == SpriteTextureFilter.Point,
    "Background filter does not match explicit Unity Point filtering.");
Assert(BirdHellVisuals.Enemy.Frames.All(frame => frame.Region.Filter == SpriteTextureFilter.Point),
    "Eagle filters do not match explicit Unity Point filtering.");
Assert(BirdHellVisuals.PlayerIdle.Frames.Concat(BirdHellVisuals.PlayerRun.Frames)
        .Concat(BirdHellVisuals.PlayerJump.Frames).Concat(BirdHellVisuals.PlayerCrouch.Frames)
        .Concat(BirdHellVisuals.PlayerRoll.Frames).Concat(BirdHellVisuals.PlayerAttack.Frames)
        .All(frame => frame.Region.Filter == SpriteTextureFilter.Linear),
    "Player filter does not match Unity's inherited Sprite default (Bilinear)." );
Assert(BirdHellVisuals.Projectile.Frames.All(frame => frame.Region.Filter == SpriteTextureFilter.Linear),
    "Projectile filter does not match Unity's inherited Sprite default (Bilinear)." );
Console.WriteLine("PASS: per-asset filtering matches verified Unity importer policy");

PixelRectangle[] projectileRectangles =
[
    new(0, 3, 68, 56),
    new(88, 2, 62, 54),
    new(171, 3, 52, 36),
    new(246, 5, 43, 16),
];
Assert(BirdHellVisuals.Projectile.Frames.Select(frame => frame.Region.Source).SequenceEqual(projectileRectangles),
    "Projectile source rectangles do not exactly match the audited Unity slices.");
Assert(BirdHellVisuals.Projectile.Frames.All(frame => frame.Region.Pivot == Vector2.Zero),
    "Projectile frames did not preserve bottom-left Unity pivots.");
Assert(BirdHellVisuals.Projectile.Frames.All(frame => frame.Region.CoordinateOrigin == PixelCoordinateOrigin.BottomLeft),
    "Projectile source rectangles did not retain Unity's bottom-left coordinate semantics.");
CheckLoop(BirdHellVisuals.Projectile, [0, 1, 2, 3], 1f / 18f, "projectile");
Assert(WorldAnimationState.ProjectileFlip(1f) == SpriteFlip.None &&
       WorldAnimationState.ProjectileFlip(-1f) == SpriteFlip.Horizontal,
    "Projectile direction did not map to visual orientation.");
Console.WriteLine("PASS: projectile rectangles, 18 fps loop, bottom-left pivots, and orientation");

player.Reset(new PlayerVisualSnapshot(false, true, false, 0f));
Assert(!player.ShouldRender, "PlayerDead presentation still renders the normal player sprite.");
var world = new WorldAnimationState();
world.Player.Update(new PlayerVisualSnapshot(true, true, false, 0f), true, false, 0.02f);
world.AdvanceEnemies(0.2f);
world.Reset(new PlayerVisualSnapshot(true, true, false, 0f));
Assert(world.Player.State == PlayerVisualState.Idle && world.Player.Playback.CurrentFrameIndex == 0,
    "R/F5-style reset did not clear attack and restart player animation.");
Assert(world.Enemy.CurrentFrameIndex == 0, "R/F5-style reset did not deterministically restart enemy animation.");
world.Reset(new PlayerVisualSnapshot(true, true, true, 0f));
Assert(world.Player.State == PlayerVisualState.Crouch,
    "Reset did not honor physically-held crouch input through normal input polling.");
var entityAssembly = typeof(Player).Assembly;
foreach (var type in new[] { typeof(PlayerAnimationState), typeof(WorldAnimationState), typeof(AnimationPlayback) })
foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
    Assert(field.FieldType.Namespace != typeof(Player).Namespace || field.FieldType.Assembly != entityAssembly,
        $"Animation model field {type.Name}.{field.Name} retains a gameplay entity reference.");
Console.WriteLine("PASS: death visibility, atomic reset behavior, and entity-reference-free animation state");
Console.WriteLine("All Milestone 8 sprite presentation checks passed.");

static void CheckLoop(SpriteAnimationClip clip, int[] sourceIndices, float frameDuration, string name)
{
    Assert(clip.Looping, $"{name} should loop.");
    CheckFrames(clip, sourceIndices, frameDuration, name);
    Assert(clip.GetFrameIndex(clip.Duration + frameDuration * 0.5f) == 0, $"{name} did not loop to frame zero.");
}

static void CheckFrames(SpriteAnimationClip clip, int[] expectedSourceIndices, float frameDuration, string name)
{
    Assert(clip.Frames.Count == expectedSourceIndices.Length, $"{name} frame count is wrong.");
    for (var index = 0; index < clip.Frames.Count; index++)
    {
        AssertNear(clip.Frames[index].Duration, frameDuration, $"{name} frame {index} duration is wrong.");
        Assert(clip.GetFrameIndex(index * frameDuration + frameDuration * 0.5f) == index,
            $"{name} did not select frame {index} at its midpoint.");
    }
    if (clip.Frames[0].Region.TextureId == BirdHellVisuals.PlayerTexture)
    {
        var actual = clip.Frames.Select(frame =>
            (592 - frame.Region.Source.Y - frame.Region.Source.Height) / 37 * 7 + frame.Region.Source.X / 50).ToArray();
        Assert(actual.SequenceEqual(expectedSourceIndices), $"{name} source frame sequence is wrong.");
    }
}

static void AssertNear(float actual, float expected, string message)
{
    if (MathF.Abs(actual - expected) > tolerance)
        throw new InvalidOperationException($"{message} Expected {expected:R}, got {actual:R}.");
}

static void AssertVector(Vector2 actual, Vector2 expected, string message)
{
    if (Vector2.Distance(actual, expected) > tolerance)
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
