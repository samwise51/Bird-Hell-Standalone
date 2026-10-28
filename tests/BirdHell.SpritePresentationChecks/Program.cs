using System.Numerics;
using System.Reflection;
using BirdHell.Entities;
using BirdHell.Presentation;
using BirdHell.Systems;
using BirdHell.World;
using BirdHell.World.Geometry;
using Microsoft.Xna.Framework.Graphics;

const float tolerance = 0.00001f;

Assert(CaveMaterialPresentation.TexturePath == "Sprites/Terrain/cave_base_material_v1.png",
    "Cave rendering does not select the dedicated canonical runtime material.");
Assert(CaveMaterialPresentation.MaterialWorldWidth == 6f &&
       CaveMaterialPresentation.MaterialWorldHeight == 6f,
    "Cave material world repeat dimensions changed or are invalid.");
Assert(CaveMaterialPresentation.Filter == TextureFilter.Linear &&
       CaveMaterialPresentation.AddressU == TextureAddressMode.Wrap &&
       CaveMaterialPresentation.AddressV == TextureAddressMode.Wrap,
    "Cave material metadata does not require linear filtering and wrapping on both axes.");
Assert(CaveMaterialPresentation.SurfaceCapTexturePath == "Sprites/Terrain/cave_surface_cap_v1.png",
    "Cave rendering does not select the canonical surface-cap ribbon.");
Assert(CaveMaterialPresentation.SurfaceCapWorldRepeatLength == 2.75f &&
       CaveMaterialPresentation.SurfaceCapOpenOverhang == 0.18f &&
       CaveMaterialPresentation.SurfaceCapSolidDepth == 0.62f &&
       CaveMaterialPresentation.MinimumSurfaceOpenNormalY == 0.35f,
    "Surface-cap world placement metadata differs from the canonical manifest.");
Assert(CaveMaterialPresentation.SurfaceCapAddressV == TextureAddressMode.Clamp,
    "Surface cap must wrap along the boundary and clamp across its depth.");
var baseUv = CaveMaterialPresentation.WorldToUv(3f, -12f);
var repeatedUvX = CaveMaterialPresentation.WorldToUv(
    3f + CaveMaterialPresentation.MaterialWorldWidth, -12f);
var repeatedUvY = CaveMaterialPresentation.WorldToUv(
    3f, -12f + CaveMaterialPresentation.MaterialWorldHeight);
AssertVector(baseUv, new Vector2(0.5f, -2f), "Known cave world position mapped to the wrong UV.");
AssertVector(repeatedUvX, baseUv + Vector2.UnitX,
    "One material width in world space did not advance U by exactly one.");
AssertVector(repeatedUvY, baseUv + Vector2.UnitY,
    "One material height in world space did not advance V by exactly one.");

var materialTiles = new CaveTile[2, 2];
for (var x = 0; x < 2; x++)
for (var y = 0; y < 2; y++)
    materialTiles[x, y] = CaveTile.Wall;
var materialGeometry = new CaveGeometryBuilder().Build(new CaveMap(materialTiles));
var materialVertices = CaveMaterialPresentation.BuildVertices(materialGeometry);
Assert(materialVertices.Length == materialGeometry.Vertices.Count &&
       materialVertices.All(vertex => vertex.GetType() == typeof(VertexPositionTexture)),
    "Cave render data does not contain exactly one textured vertex per authoritative world vertex.");
var sharedVertexIndex = materialGeometry.Triangles
    .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
    .GroupBy(index => index)
    .First(group => group.Count() > 1).Key;
var sharedWorldVertex = materialGeometry.Vertices[sharedVertexIndex];
var sharedUv = CaveMaterialPresentation.WorldToUv(sharedWorldVertex.X, sharedWorldVertex.Y);
AssertVector(
    new Vector2(materialVertices[sharedVertexIndex].TextureCoordinate.X,
        materialVertices[sharedVertexIndex].TextureCoordinate.Y),
    sharedUv,
    "Adjacent triangles do not share the same world-position-derived UV.");
Console.WriteLine("PASS: cave material path, 6x6 world mapping, textured vertices, linear wrap, and shared-vertex continuity");

var capTiles = new CaveTile[3, 3];
for (var x = 0; x < 3; x++)
for (var y = 0; y < 3; y++)
    capTiles[x, y] = x == 1 && y == 1 ? CaveTile.Open : CaveTile.Wall;
var capGeometry = new CaveGeometryBuilder().Build(new CaveMap(capTiles));
var capGeometryVerticesBefore = capGeometry.Vertices.ToArray();
var capGeometryTrianglesBefore = capGeometry.Triangles.ToArray();
var capVertices = CaveMaterialPresentation.BuildSurfaceCapVertices(capGeometry);
var repeatedCapVertices = CaveMaterialPresentation.BuildSurfaceCapVertices(capGeometry);
Assert(capVertices.Length > 0 && capVertices.Length % 6 == 0,
    "Walkable cave boundaries did not produce complete surface-cap quads.");
Assert(capVertices.SequenceEqual(repeatedCapVertices),
    "Repeated surface-cap cache builds from one geometry were not deterministic.");
Assert(capVertices.All(vertex => float.IsFinite(vertex.Position.X) &&
                                 float.IsFinite(vertex.Position.Y) &&
                                 float.IsFinite(vertex.TextureCoordinate.X) &&
                                 vertex.TextureCoordinate.Y is 0f or 1f),
    "Surface-cap placement produced non-finite data or UVs outside the ribbon depth.");
Assert(capGeometry.Vertices.SequenceEqual(capGeometryVerticesBefore) &&
       capGeometry.Triangles.SequenceEqual(capGeometryTrianglesBefore),
    "Building presentation-only surface caps mutated authoritative cave geometry.");
Console.WriteLine("PASS: deterministic surface-cap quads, coherent U-wrap/V-clamp UVs, and geometry isolation");
var fixedCapGeometry = new CaveGeometryBuilder().Build(
    new CaveGenerator().Generate("milestone-17-geometry-baseline"));
var fixedCapVertices = CaveMaterialPresentation.BuildSurfaceCapVertices(fixedCapGeometry);
Assert(fixedCapVertices.Length == 67 * 6,
    "Fixed-seed surface-cap placement count changed from the reviewed Milestone 18 baseline.");
Assert(fixedCapVertices.SequenceEqual(CaveMaterialPresentation.BuildSurfaceCapVertices(fixedCapGeometry)),
    "Fixed-seed surface-cap placement did not reproduce exactly.");
Console.WriteLine($"PASS: fixed-seed surface-cap placement produced {fixedCapVertices.Length / 6} deterministic quads");

Assert(CaveMaterialPresentation.CeilingCapTexturePath == "Sprites/Terrain/cave_ceiling_cap_v1.png" &&
       CaveMaterialPresentation.WallEdgeTexturePath == "Sprites/Terrain/cave_wall_edge_v1.png" &&
       CaveMaterialPresentation.BoulderTexturePath == "Sprites/Terrain/cave_boulder_small_v1.png" &&
       CaveMaterialPresentation.StalagmiteTexturePath == "Sprites/Terrain/cave_stalagmite_cluster_v1.png" &&
       CaveMaterialPresentation.StalactiteTexturePath == "Sprites/Terrain/cave_stalactite_single_v1.png",
    "Cave asset paths do not select the canonical runtime textures.");
Assert(CaveMaterialPresentation.CeilingCapWorldRepeatLength == 3.25f &&
       CaveMaterialPresentation.CeilingCapOpenOverhang == 0.55f &&
       CaveMaterialPresentation.CeilingCapSolidDepth == 0.30f,
    "Original Milestone 19 ceiling ribbon dimensions changed.");
Assert(CaveMaterialPresentation.ClassifyBoundary(new Vector2(0f, 1f)) == CaveBoundaryCategory.Floor &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(0f, -1f)) == CaveBoundaryCategory.Ceiling &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(-1f, 0f)) == CaveBoundaryCategory.WallLeft &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(1f, 0f)) == CaveBoundaryCategory.WallRight &&
       CaveMaterialPresentation.ClassifyBoundary(Vector2.Normalize(new Vector2(1f, -0.43f))) == CaveBoundaryCategory.Slope,
    "Representative floor, ceiling, wall, or slope-oriented normals were misclassified.");
Assert(CaveMaterialPresentation.ClassifyBoundary(new Vector2(0f, 0.3499f)) == CaveBoundaryCategory.Slope &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(0f, 0.35f)) == CaveBoundaryCategory.Floor &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(0f, -0.4499f)) == CaveBoundaryCategory.Slope &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(0f, -0.45f)) == CaveBoundaryCategory.Ceiling &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(0.8f, 0f)) == CaveBoundaryCategory.WallRight &&
       CaveMaterialPresentation.ClassifyBoundary(new Vector2(0.7999f, 0f)) == CaveBoundaryCategory.Slope,
    "Named boundary thresholds do not have deterministic near-threshold behavior.");
Console.WriteLine("PASS: mutually exclusive boundary classification covers floor, ceiling, both walls, slopes, and thresholds");

var fixedGeometryVerticesBefore = fixedCapGeometry.Vertices.ToArray();
var fixedGeometryTrianglesBefore = fixedCapGeometry.Triangles.ToArray();
var fixedBoundaries = CaveMaterialPresentation.AnalyzeBoundaries(fixedCapGeometry);
var ceilingVertices = CaveMaterialPresentation.BuildCeilingCapVertices(fixedBoundaries);
var wallVertices = CaveMaterialPresentation.BuildWallEdgeVertices(fixedCapGeometry);
Assert(ceilingVertices.Length == 48 * 6 && ceilingVertices.Length % 6 == 0,
    "Fixed-seed ceiling ribbon count changed from the original Milestone 19 baseline.");
Assert(ceilingVertices.SequenceEqual(CaveMaterialPresentation.BuildCeilingCapVertices(fixedBoundaries)),
    "Repeated ceiling ribbon cache builds were not deterministic.");
Assert(wallVertices.Length > 0 && wallVertices.Length % 6 == 0 &&
       wallVertices.SequenceEqual(CaveMaterialPresentation.BuildWallEdgeVertices(fixedCapGeometry)),
    "Fixed cave did not produce deterministic complete wall ribbon quads.");
Console.WriteLine($"PASS: original continuous ceiling ribbon produced {ceilingVertices.Length / 6} deterministic quads");
var fixedBoulders = CaveMaterialPresentation.BuildBoulderPlacements(fixedCapGeometry, "milestone-17-geometry-baseline");
var repeatedBoulders = CaveMaterialPresentation.BuildBoulderPlacements(fixedCapGeometry, "milestone-17-geometry-baseline");
var alternateBoulders = CaveMaterialPresentation.BuildBoulderPlacements(fixedCapGeometry, "milestone-19-alternate-visual-seed");
Assert(fixedBoulders.SequenceEqual(repeatedBoulders),
    "Identical seed and geometry did not reproduce boulder positions and variants exactly.");
Assert(!fixedBoulders.SequenceEqual(alternateBoulders),
    "Different visual seeds did not vary deterministic boulder placement.");
Assert(fixedBoulders.Length is > 0 and <= CaveMaterialPresentation.BoulderMaximumCount &&
       fixedBoulders.All(instance => instance.AssetVariant == 0),
    "Boulder placement is empty, exceeds its cap, or selected an unknown variant.");
Assert(fixedBoulders.Length == 3,
    "Fixed-seed boulder count changed from the reviewed Milestone 19 baseline.");
var spawnClearedBoulders = CaveMaterialPresentation.BuildBoulderPlacements(
    fixedCapGeometry, "milestone-17-geometry-baseline", fixedBoulders[0].Position);
Assert(spawnClearedBoulders.All(instance =>
        Vector2.Distance(instance.Position, fixedBoulders[0].Position) >= CaveMaterialPresentation.BoulderSpawnClearance),
    "Boulder placement did not preserve its configured spawn-area clearance.");
Assert(CaveMaterialPresentation.BuildBoulderVertices(fixedBoulders).Length == fixedBoulders.Length * 6,
    "Boulder placement did not produce exactly one cached quad per decoration.");
var fixedStalagmites = CaveMaterialPresentation.BuildStalagmitePlacements(
    fixedBoundaries, "milestone-17-geometry-baseline", null, fixedBoulders);
var repeatedStalagmites = CaveMaterialPresentation.BuildStalagmitePlacements(
    fixedBoundaries, "milestone-17-geometry-baseline", null, fixedBoulders);
var alternateStalagmites = CaveMaterialPresentation.BuildStalagmitePlacements(
    fixedBoundaries, "milestone-20-alternate-visual-seed", null, fixedBoulders);
var fixedStalactites = CaveMaterialPresentation.BuildStalactitePlacements(
    fixedBoundaries, "milestone-17-geometry-baseline");
var repeatedStalactites = CaveMaterialPresentation.BuildStalactitePlacements(
    fixedBoundaries, "milestone-17-geometry-baseline");
var alternateStalactites = CaveMaterialPresentation.BuildStalactitePlacements(
    fixedBoundaries, "milestone-20-alternate-visual-seed");
Assert(fixedStalagmites.SequenceEqual(repeatedStalagmites) &&
       fixedStalactites.SequenceEqual(repeatedStalactites),
    "Identical seed and geometry did not reproduce Milestone 20 formations exactly.");
Assert(!fixedStalagmites.SequenceEqual(alternateStalagmites) &&
       !fixedStalactites.SequenceEqual(alternateStalactites),
    "Different visual seeds did not vary both Milestone 20 formation categories.");
Assert(fixedStalagmites.Length is > 0 and <= CaveMaterialPresentation.StalagmiteMaximumCount &&
       fixedStalagmites.All(instance =>
           instance.OpenNormal.Y >= CaveMaterialPresentation.StalagmiteMinimumOpenNormalY &&
           instance.Scale is >= CaveMaterialPresentation.StalagmiteMinimumScale and <= CaveMaterialPresentation.StalagmiteMaximumScale),
    $"Stalagmites were absent, exceeded their cap, used an invalid surface, or exceeded their scale range (count={fixedStalagmites.Length}).");
Assert(fixedStalactites.Length is > 0 and <= CaveMaterialPresentation.StalactiteMaximumCount &&
       fixedStalactites.All(instance =>
           instance.OpenNormal.Y <= CaveMaterialPresentation.StalactiteMaximumOpenNormalY &&
           instance.Scale is >= CaveMaterialPresentation.StalactiteMinimumScale and <= CaveMaterialPresentation.StalactiteMaximumScale),
    $"Stalactites were absent, exceeded their cap, used an invalid surface, or exceeded their scale range (count={fixedStalactites.Length}).");
Assert(fixedStalagmites.All(instance => fixedBoulders.All(boulder =>
        Vector2.Distance(instance.Position, boulder.Position) >= CaveMaterialPresentation.FormationOverlapClearance)),
    "A stalagmite cluster overlapped an existing boulder placement.");
Assert(CaveMaterialPresentation.BuildStalagmiteVertices(fixedStalagmites).Length == fixedStalagmites.Length * 6 &&
       CaveMaterialPresentation.BuildStalactiteVertices(fixedStalactites).Length == fixedStalactites.Length * 6,
    "A Milestone 20 formation did not produce exactly one cached render quad.");
var invalidFormationSegments = new[]
{
    new CaveBoundarySegment(0, 0, new Vector2(0f, 0f), new Vector2(2f, 0f), Vector2.UnitX,
        -Vector2.UnitY, 2f, CaveBoundaryCategory.Floor),
    new CaveBoundarySegment(0, 1, new Vector2(3f, 0f), new Vector2(5f, 0f), Vector2.UnitX,
        Vector2.UnitY, 2f, CaveBoundaryCategory.Ceiling),
};
Assert(CaveMaterialPresentation.BuildStalagmitePlacements(invalidFormationSegments, "invalid-floor").Length == 0 &&
       CaveMaterialPresentation.BuildStalactitePlacements(invalidFormationSegments, "invalid-ceiling").Length == 0,
    "A Milestone 20 formation accepted a clearly invalid surface orientation.");
Assert(fixedCapGeometry.Vertices.SequenceEqual(fixedGeometryVerticesBefore) &&
       fixedCapGeometry.Triangles.SequenceEqual(fixedGeometryTrianglesBefore),
    "Presentation cache construction mutated authoritative geometry.");
var categoryCounts = Enum.GetValues<CaveBoundaryCategory>()
    .ToDictionary(category => category, category => fixedBoundaries.Count(segment => segment.Category == category));
Assert(categoryCounts[CaveBoundaryCategory.Floor] == 67 &&
       categoryCounts[CaveBoundaryCategory.Ceiling] == 48 &&
       categoryCounts[CaveBoundaryCategory.WallLeft] == 2 &&
       categoryCounts[CaveBoundaryCategory.WallRight] == 7 &&
       categoryCounts[CaveBoundaryCategory.Slope] == 0,
    "Fixed-seed boundary category counts changed from the Milestone 19 baseline.");
Console.WriteLine($"PASS: fixed-seed boundary counts floor={categoryCounts[CaveBoundaryCategory.Floor]}, " +
                  $"ceiling={categoryCounts[CaveBoundaryCategory.Ceiling]}, left-wall={categoryCounts[CaveBoundaryCategory.WallLeft]}, " +
                  $"right-wall={categoryCounts[CaveBoundaryCategory.WallRight]}, slope={categoryCounts[CaveBoundaryCategory.Slope]}");
Console.WriteLine($"PASS: deterministic visual-only boulder placement produced {fixedBoulders.Length} fixed-seed props and " +
                  $"{alternateBoulders.Length} alternate-seed props without gameplay RNG");
Console.WriteLine($"PASS: deterministic visual-only formations produced {fixedStalagmites.Length} stalagmites and " +
                  $"{fixedStalactites.Length} stalactites on valid surfaces without mutating geometry");

Assert(BirdHellVisuals.PlayerTexture == "Sprites/Player/player_explorer_runtime_v1.png",
    "Production player texture path does not select the normalized explorer atlas.");
Assert(BirdHellVisuals.PlayerClips.Count == 7 &&
       BirdHellVisuals.PlayerClips.Select(clip => clip.Name).SequenceEqual(
           new[] { "Idle", "Run", "Jump", "Airborne", "Crouch", "Roll", "Attack" }),
    "The normalized player atlas does not expose exactly seven player clips.");
CheckPlayerClip(BirdHellVisuals.PlayerIdle, 0, 0, 9, 1f / 7f, true);
CheckPlayerClip(BirdHellVisuals.PlayerRun, 1, 0, 8, 1f / 12f, true);
Assert(BirdHellVisuals.PlayerRun.Frames[0].Region.Source != BirdHellVisuals.PlayerIdle.Frames[0].Region.Source,
    "Production Run still uses the temporary Idle fallback.");
CheckPlayerClip(BirdHellVisuals.PlayerJump, 2, 0, 6, 1f / 10f, false);
CheckPlayerClip(BirdHellVisuals.PlayerAirborne, 3, 0, 1, 1f, true);
CheckPlayerClip(BirdHellVisuals.PlayerCrouch, 3, 1, 1, 1f, true);
CheckPlayerClip(BirdHellVisuals.PlayerRoll, 4, 0, 6, 1f / 12f, false);
CheckPlayerClip(BirdHellVisuals.PlayerAttack, 5, 0, 7, 1f / 14f, false);
AssertNear(BirdHellVisuals.PlayerRoll.Duration, 0.5f, "Roll duration is not 0.5 seconds.");
AssertNear(BirdHellVisuals.PlayerRoll.Duration, PlayerMovementSystem.SecondJumpDuration,
    "Roll presentation duration is not synchronized with the core second-jump duration.");
AssertNear(BirdHellVisuals.PlayerAttack.Duration, 0.5f, "Attack duration is not 0.5 seconds.");
var jumpPlayback = new AnimationPlayback(BirdHellVisuals.PlayerJump);
jumpPlayback.Advance(BirdHellVisuals.PlayerJump.Duration * 2f);
Assert(jumpPlayback.IsFinished && jumpPlayback.CurrentFrameIndex == 5,
    "Jump did not stop on its sixth and final frame.");
var staticPlayback = new AnimationPlayback(BirdHellVisuals.PlayerAirborne);
staticPlayback.Advance(12f);
Assert(staticPlayback.CurrentFrameIndex == 0, "The Airborne pose is not static.");
Console.WriteLine("PASS: normalized atlas path and approved eight-frame replacement Run cycle");

AssertVector(BirdHellVisuals.PlayerIdle.Frames[0].Region.WorldSize, new Vector2(2.304f, 2.112f),
    "Player cell world dimensions do not use the named normalized-atlas scale.");
Assert(BirdHellVisuals.EnemyTexture == "Sprites/Enemy/enemy_bird_purple_flight_runtime_v1.png",
    "Production enemy texture path does not select the normalized purple-bird atlas.");
Assert(BirdHellVisuals.EnemyAtlasWidth == 512 && BirdHellVisuals.EnemyAtlasHeight == 480 &&
       BirdHellVisuals.EnemyCellWidth == 128 && BirdHellVisuals.EnemyCellHeight == 120,
    "Purple-bird atlas/cell dimensions do not match the manifest.");
Assert(BirdHellVisuals.Enemy.Frames.Count == 16 && BirdHellVisuals.Enemy.Looping,
    "Purple-bird Flight must contain 16 looping frames.");
for (var index = 0; index < BirdHellVisuals.Enemy.Frames.Count; index++)
{
    var expected = new PixelRectangle(index % 4 * 128, index / 4 * 120, 128, 120);
    var frame = BirdHellVisuals.Enemy.Frames[index];
    Assert(frame.Region.TextureId == BirdHellVisuals.EnemyTexture && frame.Region.Source == expected,
        $"Purple-bird frame {index} does not map to its row-major 4x4 cell.");
    AssertNear(frame.Duration, 1f / 12f, $"Purple-bird frame {index} duration is wrong.");
}
AssertVector(BirdHellVisuals.Enemy.Frames[0].Region.Pivot, new Vector2(80f / 128f, 72f / 120f),
    "Purple-bird normalized pivot does not match manifest (80,72).");
AssertVector(BirdHellVisuals.Enemy.Frames[0].Region.WorldSize, new Vector2(1.664f, 1.56f),
    "Purple-bird cell world dimensions do not use the named world scale.");
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
var enemyVisibleHeightAt720p = BirdHellVisuals.EnemyReferenceMaximumVisibleHeightPixels *
                               BirdHellVisuals.EnemyWorldScale / BirdHellVisuals.PixelsPerUnit * 72f;
Assert(enemyVisibleHeightAt720p is >= 80f and <= 95f,
    "Named bird scale does not put the tallest measured wing pose in the 80-95px target at 720p.");
AssertNear(Enemy.OriginalColliderRadius, 0.45807048f, "Enemy collider radius changed during presentation work.");
AssertVector(Enemy.OriginalColliderOffset, new Vector2(-0.15384216f, -0.07692099f),
    "Enemy collider offset changed during presentation work.");
Console.WriteLine("PASS: player and purple-bird pivots, named scales, display sizes, and unchanged enemy collider");

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
Assert(player.State == PlayerVisualState.Airborne && player.Playback.CurrentFrameIndex == 0,
    "Completed Roll did not transition directly to the normal Airborne presentation.");
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
CheckLoop(BirdHellVisuals.Enemy, Enumerable.Range(0, 16).ToArray(), 1f / 12f, "enemy");
Assert(BirdHellVisuals.EnemyNativeFacing == HorizontalFacing.Right,
    "Presentation metadata does not represent the purple-bird artwork's native right-facing orientation.");
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
Assert(facingEnemy.FacingDirection == 1 && WorldAnimationState.EnemyFlip(facingEnemy.FacingDirection) == SpriteFlip.None,
    "Meaningful positive movement did not render the native-right bird facing right.");
facingEnemy.UpdateFacingFromHorizontalMovement(-0.02f);
Assert(facingEnemy.FacingDirection == -1 && WorldAnimationState.EnemyFlip(facingEnemy.FacingDirection) == SpriteFlip.Horizontal,
    "Meaningful negative movement did not render the native-right bird facing left.");
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
Assert(BirdHellVisuals.Enemy.Frames.All(frame => frame.Region.Filter == SpriteTextureFilter.Linear),
    "Purple-bird atlas does not use manifest-required linear filtering.");
Assert(BirdHellVisuals.PlayerClips.SelectMany(clip => clip.Frames)
        .All(frame => frame.Region.Filter == SpriteTextureFilter.Linear),
    "Player atlas is not entirely linear filtered.");
Assert(BirdHellVisuals.Projectile.Frames.All(frame => frame.Region.Filter == SpriteTextureFilter.Linear),
    "Projectile filter does not match Unity's inherited Sprite default (Bilinear).");
Assert(SpritePresentation.RequiresPremultipliedAlphaUpload(BirdHellVisuals.PlayerTexture) &&
       SpritePresentation.RequiresPremultipliedAlphaUpload(BirdHellVisuals.EnemyTexture) &&
       !SpritePresentation.RequiresPremultipliedAlphaUpload(BirdHellVisuals.ProjectileTexture),
    "Straight-alpha runtime atlas upload policy is wrong or changed an unrelated texture.");
Console.WriteLine("PASS: per-asset filtering and scoped straight-alpha premultiplication policy");

foreach (var clip in BirdHellVisuals.PlayerClips)
{
    Assert(HitFeedbackPresentation.TintFor(0f) == HitFeedbackPresentation.NormalTint &&
           HitFeedbackPresentation.TintFor(0.1f) == HitFeedbackPresentation.DamageTint,
        $"{clip.Name} does not retain normal/damage tint selection.");
}
Assert(BirdHellVisuals.Enemy.Frames.All(_ =>
           HitFeedbackPresentation.TintFor(0f) == HitFeedbackPresentation.NormalTint &&
           HitFeedbackPresentation.TintFor(0.1f) == HitFeedbackPresentation.DamageTint),
    "A purple-bird flight frame is incompatible with normal/damage tint selection.");
Console.WriteLine("PASS: every player state and all 16 bird frames retain damage-flash tinting");

const string timingSeed = "milestone-15-phase-check";
var parameters = Enumerable.Range(0, 5)
    .Select(id => EnemyFlightVisualTiming.ForEnemy(timingSeed, id))
    .ToArray();
var repeatedParameters = Enumerable.Range(0, 5)
    .Select(id => EnemyFlightVisualTiming.ForEnemy(timingSeed, id))
    .ToArray();
Assert(parameters.SequenceEqual(repeatedParameters),
    "Same run seed and enemy IDs did not reproduce flight visual parameters.");
Assert(parameters.Select(item => item.PhaseOffset).Distinct().Count() == 5 &&
       parameters.All(item => item.PhaseOffset >= 0f && item.PhaseOffset < BirdHellVisuals.Enemy.Duration),
    "Representative enemy IDs did not receive stable, varied in-cycle phases.");
Assert(parameters.All(item => item.RateMultiplier >= EnemyFlightVisualTiming.MinimumRateMultiplier &&
                              item.RateMultiplier <= EnemyFlightVisualTiming.MaximumRateMultiplier),
    "A deterministic bird flap-rate multiplier is outside the allowed range.");
var averageRate = Enumerable.Range(0, 100)
    .Select(id => EnemyFlightVisualTiming.ForEnemy(timingSeed, id).RateMultiplier)
    .Average();
Assert(Math.Abs(averageRate - 1d) < 0.01d, "Deterministic bird rates are not centered near nominal 1.0x.");
var initialFrames = Enumerable.Range(0, 5)
    .Select(id => EnemyFlightVisualTiming.FrameIndexAt(timingSeed, id, 0d))
    .ToArray();
Assert(initialFrames.Distinct().Count() >= 3,
    "Five representative birds occupy fewer than three initial Flight frames.");
foreach (var id in Enumerable.Range(0, 5))
{
    const double visualTime = 4.25d;
    var first = EnemyFlightVisualTiming.FrameIndexAt(timingSeed, id, visualTime);
    var second = EnemyFlightVisualTiming.FrameIndexAt(timingSeed, id, visualTime);
    var rate = EnemyFlightVisualTiming.ForEnemy(timingSeed, id).RateMultiplier;
    var wrapped = EnemyFlightVisualTiming.FrameIndexAt(
        timingSeed, id, visualTime + BirdHellVisuals.Enemy.Duration / rate);
    Assert(first == second && first == wrapped,
        $"Enemy {id} frame selection is not deterministic or did not wrap its rate-adjusted cycle.");
}
Console.WriteLine("PASS: deterministic varied phases, bounded subtle rates, frame selection, and looping");

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
var timeBeforePause = world.EnemyVisualTime;
var frameBeforePause = world.EnemyFrame(timingSeed, 2);
Assert(world.EnemyVisualTime == timeBeforePause && world.EnemyFrame(timingSeed, 2) == frameBeforePause,
    "A paused presentation changed enemy visual time/frame without a fixed-step advance.");
world.Reset(new PlayerVisualSnapshot(true, true, false, 0f));
Assert(world.Player.State == PlayerVisualState.Idle && world.Player.Playback.CurrentFrameIndex == 0,
    "R/F5-style reset did not clear attack and restart player animation.");
Assert(world.EnemyVisualTime == 0d &&
       world.EnemyFrame(timingSeed, 2) ==
       BirdHellVisuals.Enemy.Frames[EnemyFlightVisualTiming.FrameIndexAt(timingSeed, 2, 0d)],
    "R/F5-style reset did not restart deterministic enemy visual time/phase.");
world.Reset(new PlayerVisualSnapshot(true, true, true, 0f));
Assert(world.Player.State == PlayerVisualState.Crouch,
    "Reset did not honor physically-held crouch input through normal input polling.");
var entityAssembly = typeof(Player).Assembly;
foreach (var type in new[]
         {
             typeof(PlayerAnimationState), typeof(WorldAnimationState), typeof(AnimationPlayback),
             typeof(EnemyFlightVisualParameters), typeof(EnemyFlightVisualTiming),
         })
foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
    Assert(field.FieldType.Namespace != typeof(Player).Namespace || field.FieldType.Assembly != entityAssembly,
        $"Animation model field {type.Name}.{field.Name} retains a gameplay entity reference.");
Console.WriteLine("PASS: pause freeze, reset behavior, stateless dead-enemy handling, and domain isolation");
Console.WriteLine("All Milestone 15 sprite presentation checks passed.");

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
