using System.Numerics;
using System.Reflection;
using BirdHell.Entities;
using BirdHell.Presentation;
using BirdHell.Systems;

const float tolerance = 0.00001f;

Assert(BirdHellVisuals.PlayerTexture == "Sprites/Player/player_explorer_runtime_v1.png",
    "Production player texture path does not select the normalized explorer atlas.");
Assert(BirdHellVisuals.PlayerClips.Count == 7 &&
       BirdHellVisuals.PlayerClips.Select(clip => clip.Name).SequenceEqual(
           new[] { "Idle", "Run", "Jump", "Airborne", "Crouch", "Roll", "Attack" }),
    "The normalized player atlas does not expose exactly seven player clips.");
CheckPlayerClip(BirdHellVisuals.PlayerIdle, 0, 0, 9, 1f / 7f, true);
CheckPlayerClip(BirdHellVisuals.PlayerRun, 0, 0, 1, 1f, true);
Assert(BirdHellVisuals.PlayerRun.Frames[0].Region.Source == BirdHellVisuals.PlayerIdle.Frames[0].Region.Source,
    "Temporary Run fallback does not use the stable first Idle pose.");
CheckPlayerClip(BirdHellVisuals.PlayerJump, 2, 0, 6, 1f / 10f, false);
CheckPlayerClip(BirdHellVisuals.PlayerAirborne, 3, 0, 1, 1f, true);
CheckPlayerClip(BirdHellVisuals.PlayerCrouch, 3, 1, 1, 1f, true);
CheckPlayerClip(BirdHellVisuals.PlayerRoll, 4, 0, 6, 1f / 12f, false);
CheckPlayerClip(BirdHellVisuals.PlayerAttack, 5, 0, 7, 1f / 14f, false);
AssertNear(BirdHellVisuals.PlayerRoll.Duration, 0.5f, "Roll duration is not 0.5 seconds.");
AssertNear(BirdHellVisuals.PlayerAttack.Duration, 0.5f, "Attack duration is not 0.5 seconds.");
var jumpPlayback = new AnimationPlayback(BirdHellVisuals.PlayerJump);
jumpPlayback.Advance(BirdHellVisuals.PlayerJump.Duration * 2f);
Assert(jumpPlayback.IsFinished && jumpPlayback.CurrentFrameIndex == 5,
    "Jump did not stop on its sixth and final frame.");
var staticPlayback = new AnimationPlayback(BirdHellVisuals.PlayerAirborne);
staticPlayback.Advance(12f);
Assert(staticPlayback.CurrentFrameIndex == 0, "The Airborne pose is not static.");
Console.WriteLine("PASS: normalized atlas path, six approved clips, and isolated one-frame Run fallback");

AssertVector(BirdHellVisuals.PlayerIdle.Frames[0].Region.WorldSize, new Vector2(2.304f, 2.112f),
    "Player cell world dimensions do not use the named normalized-atlas scale.");
AssertVector(BirdHellVisuals.Enemy.Frames[0].Region.WorldSize, new Vector2(1.2f, 1.23f),
    "Enemy PPU/root-scale world dimensions are wrong.");
var bodyPivot = new Vector2(0.5f, 0.5f);
Assert(BirdHellVisuals.PlayerClips.SelectMany(clip => clip.Frames)
        .All(frame => frame.Region.Pivot == bodyPivot),
    "A player frame does not use the fixed (96,88) logical body pivot.");
Assert(BirdHellVisuals.PlayerBodyPivotPixels == new Vector2(96f, 88f) &&
       BirdHellVisuals.PlayerGroundBaselinePixels == new Vector2(96f, 160f),
    "Player pivot/baseline metadata differs from the normalized atlas.");
var visibleHeightAt720p = BirdHellVisuals.PlayerReferenceVisibleBodyHeightPixels *
                          BirdHellVisuals.PlayerWorldScale / BirdHellVisuals.PixelsPerUnit * 72f;
Assert(visibleHeightAt720p is >= 105f and <= 120f,
    "Named player scale does not put the normalized 123px body in the 105-120px target at 720p.");
Console.WriteLine("PASS: fixed body pivot, baseline metadata, and ~106px visible height at 720p");

var player = new PlayerAnimationState();
player.Reset(new PlayerVisualSnapshot(true, true, false, 0f));
Assert(player.State == PlayerVisualState.Idle, "Grounded stationary player did not select Idle.");
player.Reset(new PlayerVisualSnapshot(true, true, false, 0.011f));
Assert(player.State == PlayerVisualState.Run, "Grounded movement above 0.01 did not select Run.");
player.Reset(new PlayerVisualSnapshot(true, true, true, 0f));
Assert(player.State == PlayerVisualState.Crouch, "Grounded crouching player did not select Crouch.");
player.Reset(new PlayerVisualSnapshot(true, false, true, 8f));
Assert(player.State == PlayerVisualState.Jump, "Ordinary first-jump presentation did not begin with Jump.");
player.Update(new PlayerVisualSnapshot(true, false, true, 8f), false, false,
    BirdHellVisuals.PlayerJump.Duration);
Assert(player.State == PlayerVisualState.Airborne && player.Playback.CurrentFrameIndex == 0,
    "Completed takeoff did not transition to the dedicated static Airborne pose.");
player.Update(new PlayerVisualSnapshot(true, true, false, 0f), false, false, 0f);
Assert(player.State == PlayerVisualState.Idle,
    "Landing did not exit Airborne and return to grounded locomotion.");
Console.WriteLine("PASS: Idle, Run, Crouch, Jump, Airborne, and landing selection");

var airborneSnapshot = new PlayerVisualSnapshot(true, false, false, 0f);
player.Reset(airborneSnapshot);
player.Update(airborneSnapshot, false, false, BirdHellVisuals.PlayerJump.Duration);
Assert(player.State == PlayerVisualState.Airborne, "Sustained flight did not reach Airborne before Roll test.");
player.Update(airborneSnapshot, false, true, 0f);
Assert(player.State == PlayerVisualState.Roll && player.Playback.CurrentFrameIndex == 0,
    "Accepted second jump did not start Roll on its first frame.");
player.Update(airborneSnapshot, false, false, 0.15f);
Assert(player.State == PlayerVisualState.Roll && player.Playback.CurrentFrameIndex == 1,
    "Roll did not progress or restarted merely because the player remained airborne.");
player.Update(airborneSnapshot, true, false, 0f);
Assert(player.State == PlayerVisualState.Attack, "Attack did not retain visual priority over an active Roll.");
player.Update(airborneSnapshot, false, false, BirdHellVisuals.PlayerAttack.Duration);
Assert(player.State == PlayerVisualState.Airborne,
    "Roll did not continue underneath Attack and finish into the sustained Airborne pose.");

player.Reset(airborneSnapshot);
player.Update(airborneSnapshot, false, true, 0f);
player.Update(airborneSnapshot, false, false, BirdHellVisuals.PlayerRoll.Duration);
Assert(player.State == PlayerVisualState.Jump, "Completed early-air Roll did not return to Jump.");
player.Update(airborneSnapshot, false, false, BirdHellVisuals.PlayerJump.Duration);
Assert(player.State == PlayerVisualState.Airborne, "Completed post-Roll Jump did not settle on Airborne.");
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
Console.WriteLine("PASS: Attack > Roll > Jump/Airborne priority, Roll progression, landing, and reset semantics");

var combatAnimation = new PlayerAnimationState();
var weapon = new WeaponSystem();
var weaponState = new WeaponState();
var gameplayPlayer = new Player(Vector2.Zero);
Assert(weapon.Fire(weaponState, gameplayPlayer, true) is not null, "First valid shot was blocked.");
combatAnimation.Update(new PlayerVisualSnapshot(true, true, false, 0f), true, false, 0.2f);
Assert(combatAnimation.State == PlayerVisualState.Attack && combatAnimation.Playback.CurrentFrameIndex > 0,
    "Attack presentation did not progress independently.");
Assert(weapon.Fire(weaponState, gameplayPlayer, true) is not null && weaponState.ShotsFired == 2,
    "Long Attack presentation changed repeated-shot gameplay semantics.");
combatAnimation.Update(new PlayerVisualSnapshot(true, true, false, 0f), true, false, 0f);
Assert(combatAnimation.Playback.CurrentFrameIndex == 0,
    "A valid repeated shot did not restart Attack presentation at frame zero.");
Console.WriteLine("PASS: 0.5-second Attack restarts visually without blocking repeated projectile creation");

Assert(BirdHellVisuals.PlayerNativeFacing == HorizontalFacing.Right &&
       PlayerAnimationState.FlipForFacing(1) == SpriteFlip.None &&
       PlayerAnimationState.FlipForFacing(-1) == SpriteFlip.Horizontal,
    "Native-right player facing did not map right to unflipped and left to horizontal flip.");
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
Console.WriteLine("PASS: player and enemy native-facing mappings preserve gameplay state");

Assert(BirdHellVisuals.BackgroundFilter == SpriteTextureFilter.Point,
    "Background filter does not match explicit Unity Point filtering.");
Assert(BirdHellVisuals.Enemy.Frames.All(frame => frame.Region.Filter == SpriteTextureFilter.Point),
    "Eagle filters do not match explicit Unity Point filtering.");
Assert(BirdHellVisuals.PlayerClips.SelectMany(clip => clip.Frames)
        .All(frame => frame.Region.Filter == SpriteTextureFilter.Linear),
    "Player atlas is not entirely linear filtered.");
Assert(BirdHellVisuals.Projectile.Frames.All(frame => frame.Region.Filter == SpriteTextureFilter.Linear),
    "Projectile filter does not match Unity's inherited Sprite default (Bilinear).");
Console.WriteLine("PASS: per-asset filtering keeps the player in the linear-filtered batch");

foreach (var clip in BirdHellVisuals.PlayerClips)
{
    Assert(HitFeedbackPresentation.TintFor(0f) == HitFeedbackPresentation.NormalTint &&
           HitFeedbackPresentation.TintFor(0.1f) == HitFeedbackPresentation.DamageTint,
        $"{clip.Name} does not retain normal/damage tint selection.");
}
Console.WriteLine("PASS: every new player state remains compatible with white and damage-flash tinting");

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
Console.WriteLine("PASS: projectile presentation remains unchanged");

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
Console.WriteLine("All Milestone 14 sprite presentation checks passed.");

static void CheckPlayerClip(
    SpriteAnimationClip clip,
    int row,
    int firstColumn,
    int frameCount,
    float frameDuration,
    bool looping)
{
    Assert(clip.Looping == looping, $"{clip.Name} looping metadata is wrong.");
    Assert(clip.Frames.Count == frameCount, $"{clip.Name} frame count is wrong.");
    for (var index = 0; index < frameCount; index++)
    {
        var frame = clip.Frames[index];
        var expected = new PixelRectangle(
            (firstColumn + index) * BirdHellVisuals.PlayerCellWidth,
            row * BirdHellVisuals.PlayerCellHeight,
            BirdHellVisuals.PlayerCellWidth,
            BirdHellVisuals.PlayerCellHeight);
        Assert(frame.Region.TextureId == BirdHellVisuals.PlayerTexture && frame.Region.Source == expected,
            $"{clip.Name} frame {index} does not map to its manifest cell.");
        Assert(expected.X >= 0 && expected.Y >= 0 &&
               expected.X + expected.Width <= BirdHellVisuals.PlayerAtlasWidth &&
               expected.Y + expected.Height <= BirdHellVisuals.PlayerAtlasHeight,
            $"{clip.Name} frame {index} exceeds the atlas bounds.");
        AssertNear(frame.Duration, frameDuration, $"{clip.Name} frame {index} duration is wrong.");
        Assert(clip.GetFrameIndex(index * frameDuration + frameDuration * 0.5f) == index,
            $"{clip.Name} did not select frame {index} at its midpoint.");
    }
    var playback = new AnimationPlayback(clip);
    playback.Advance(clip.Duration);
    Assert(looping
            ? !playback.IsFinished && playback.CurrentFrameIndex == 0
            : playback.IsFinished && playback.CurrentFrameIndex == frameCount - 1,
        $"{clip.Name} completion/loop behavior is wrong.");
}

static void CheckLoop(SpriteAnimationClip clip, int[] sourceIndices, float frameDuration, string name)
{
    Assert(clip.Looping, $"{name} should loop.");
    Assert(clip.Frames.Count == sourceIndices.Length, $"{name} frame count is wrong.");
    for (var index = 0; index < clip.Frames.Count; index++)
    {
        AssertNear(clip.Frames[index].Duration, frameDuration, $"{name} frame {index} duration is wrong.");
        Assert(clip.GetFrameIndex(index * frameDuration + frameDuration * 0.5f) == index,
            $"{name} did not select frame {index} at its midpoint.");
    }
    Assert(clip.GetFrameIndex(clip.Duration + frameDuration * 0.5f) == 0,
        $"{name} did not loop to frame zero.");
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
