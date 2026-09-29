using System.Numerics;
using BirdHell.Core;
using BirdHell.Entities;
using BirdHell.Presentation;
using BirdHell.Systems;

const float tolerance = 0.0001f;

var weapon = new WeaponSystem();
var state = new WeaponState();
var player = new Player(new Vector2(2f, 3f));
var shot = weapon.Fire(state, player, true);
Assert(shot is not null, "A Fire1 press did not create a projectile.");
var firedShot = shot ?? throw new InvalidOperationException("A Fire1 press did not create a projectile.");
Assert(state.ShotsFired == 1, "Weapon state did not record the shot.");
Assert(Vector2.Distance(firedShot.Position, new Vector2(2.344f, 3f)) < tolerance, "Projectile did not spawn at the serialized fire-point offset.");
Assert(Vector2.Distance(firedShot.Direction, Vector2.UnitX) < tolerance, "Projectile did not use normalized facing direction.");
Assert(MathF.Abs(firedShot.Speed - 20f) < tolerance && firedShot.Damage == 25, "Projectile did not use serialized speed/damage.");
Assert(Vector2.Distance(firedShot.ColliderCenter, new Vector2(2.684f, 3.28f)) < tolerance,
    "Projectile did not preserve the serialized circle-collider offset.");
Console.WriteLine("PASS: Fire1 press creates the expected projectile at the serialized muzzle offset");

Assert(weapon.Fire(state, player, false) is null, "Weapon fired without a button-down edge.");
var immediateSecondShot = weapon.Fire(state, player, true);
Assert(immediateSecondShot is not null && state.ShotsFired == 2, "Original weapon incorrectly imposed a time cooldown.");
Console.WriteLine("PASS: edge-trigger firing is enforced and the original has no time cooldown");

var facingMovement = new PlayerMovementSystem(new TerrainCollisionSystem([]));
facingMovement.Step(player, new PlayerInput(-1f, false, false), 0.02f);
var leftShot = weapon.Fire(state, player, true)!;
Assert(Vector2.Distance(leftShot.Direction, -Vector2.UnitX) < tolerance, "Facing flip did not reverse projectile direction.");
Assert(Vector2.Distance(leftShot.Position, player.Position - Vector2.UnitX * 0.344f) < tolerance, "Facing flip did not reverse muzzle offset.");
Console.WriteLine("PASS: projectile direction is normalized and follows the player facing flip");

var emptyTerrain = new TerrainCollisionSystem([]);
var movementSystem = new ProjectileSystem(emptyTerrain);
var movingShot = NewProjectile(Vector2.Zero, Vector2.UnitX);
movementSystem.Spawn(movingShot);
movementSystem.Step(0.02f);
Assert(Vector2.Distance(movingShot.Position, new Vector2(0.4f, 0f)) < tolerance, "Projectile movement did not use 20 units/second.");
Console.WriteLine("PASS: projectile movement uses the serialized speed with no gravity");

var wall = new TerrainCollisionSystem([
    new TerrainSegment(new Vector2(2f, -5f), new Vector2(2f, 5f)),
]);
var tunnelingSystem = new ProjectileSystem(wall);
tunnelingSystem.Spawn(NewProjectile(Vector2.Zero, Vector2.UnitX));
tunnelingSystem.Step(0.2f);
Assert(tunnelingSystem.Projectiles.Count == 0, "Projectile tunneled through an authored terrain boundary.");
Console.WriteLine("PASS: swept projectile collision prevents tunneling and terrain impact removes the projectile");

var persistentSystem = new ProjectileSystem(emptyTerrain);
var persistentShot = NewProjectile(Vector2.Zero, Vector2.UnitX);
persistentSystem.Spawn(persistentShot);
for (var i = 0; i < 20; i++) persistentSystem.Step(0.5f);
Assert(persistentSystem.Projectiles.Count == 1 && MathF.Abs(persistentShot.Age - 10f) < tolerance,
    "A projectile expired despite the original having no lifetime.");
Console.WriteLine("PASS: original projectiles have no lifetime expiration");

var expiringSystem = new ProjectileSystem(emptyTerrain);
expiringSystem.Spawn(new Projectile(Vector2.Zero, Vector2.UnitX, 20f, 25, 0.34f, 0.34f, 0.28f, 0.05f));
expiringSystem.Step(0.03f);
Assert(expiringSystem.Projectiles.Count == 1, "Optional lifetime expired too early.");
expiringSystem.Step(0.03f);
Assert(expiringSystem.Projectiles.Count == 0, "Optional lifetime did not remove the projectile.");
Console.WriteLine("PASS: projectile lifetime support expires deterministically when configured");

var targetSystem = new ProjectileSystem(emptyTerrain);
var dummy = new DummyDamageable();
targetSystem.Spawn(NewProjectile(Vector2.Zero, Vector2.UnitX));
var targetResult = targetSystem.Step(0.1f, [new ProjectileTarget(dummy, new Vector2(1.5f, 0f), 0.2f)]);
Assert(dummy.Health == 75 && targetSystem.Projectiles.Count == 0, "Enemy-compatible target did not receive projectile damage.");
Assert(targetResult == new ProjectileStepResult(1, 0),
    "A confirmed projectile hit did not report exactly one nonfatal damage event.");
Console.WriteLine("PASS: renderer-independent damage target receives 25 damage and consumes the projectile");

var feedbackPlayer = new Player(Vector2.Zero);
var firstPlayerDamage = feedbackPlayer.TakeDamage(25);
Assert(firstPlayerDamage == new DamageResult(true, false) &&
       MathF.Abs(feedbackPlayer.DamageFlashTime - Player.DamageFlashDuration) < tolerance,
    "Confirmed player damage did not start the exact 0.5-second flash.");
feedbackPlayer.AdvanceFeedback(0.49f);
Assert(feedbackPlayer.DamageFlashTime > 0f, "Player flash ended before 0.5 seconds.");
feedbackPlayer.TakeDamage(25);
Assert(MathF.Abs(feedbackPlayer.DamageFlashTime - Player.DamageFlashDuration) < tolerance,
    "Repeated player damage did not restart the flash.");
feedbackPlayer.AdvanceFeedback(0.5f);
Assert(feedbackPlayer.DamageFlashTime == 0f, "Player flash remained active after 0.5 seconds.");
Assert(HitFeedbackPresentation.TintFor(0.1f) == HitFeedbackPresentation.DamageTint &&
       HitFeedbackPresentation.TintFor(0f) == HitFeedbackPresentation.NormalTint,
    "Flash state did not select the red and normal presentation tints.");
Console.WriteLine("PASS: player damage flash lasts 0.5 seconds, refreshes on repeat hits, and restores normal tint");

var firstBird = new Enemy(1, Vector2.Zero, 2);
var secondBird = new Enemy(2, new Vector2(10f, 0f), 2);
var birdHitSystem = new ProjectileSystem(emptyTerrain);
birdHitSystem.Spawn(NewProjectile(new Vector2(-1f, 0f), Vector2.UnitX));
var birdHit = birdHitSystem.Step(0.1f,
    [new ProjectileTarget(firstBird, Vector2.Zero, 0.2f), new ProjectileTarget(secondBird, secondBird.Position, 0.2f)]);
Assert(birdHit == new ProjectileStepResult(1, 0) &&
       firstBird.DamageFlashTime == Enemy.DamageFlashDuration && secondBird.DamageFlashTime == 0f,
    "Projectile damage did not flash only the confirmed target bird.");
firstBird.AdvanceFeedback(0.25f);
firstBird.TakeDamage(25);
Assert(firstBird.DamageFlashTime == Enemy.DamageFlashDuration,
    "Repeated bird damage did not restart the flash.");
firstBird.AdvanceFeedback(0.5f);
Assert(firstBird.DamageFlashTime == 0f, "Bird flash remained active after 0.5 seconds.");
Console.WriteLine("PASS: projectile hits flash only the damaged bird for a refreshable 0.5 seconds");

var contactPlayer = new Player(Vector2.Zero);
var contactBird = new Enemy(3, contactPlayer.ColliderCenter - Enemy.OriginalColliderOffset, 2)
{
    ContactTimer = EnemyCombatSystem.ContactDelay,
};
var contactResult = new EnemyCombatSystem().Step([contactBird], contactPlayer, 0.02f);
Assert(contactResult == new EnemyCombatStepResult(1, false),
    "Applied contact damage did not report exactly one player-hit event.");
var noContactResult = new EnemyCombatSystem().Step([contactBird], contactPlayer, 0.02f);
Assert(noContactResult.PlayerDamageEvents == 0,
    "Contact feedback triggered when the >1-second damage rule did not apply damage.");

var dyingPlayer = new Player(Vector2.Zero);
dyingPlayer.TakeDamage(75);
var lethalContactBird = new Enemy(4, dyingPlayer.ColliderCenter - Enemy.OriginalColliderOffset, 2)
{
    ContactTimer = EnemyCombatSystem.ContactDelay,
};
var lethalContact = new EnemyCombatSystem().Step([lethalContactBird], dyingPlayer, 0.02f);
Assert(lethalContact == new EnemyCombatStepResult(1, true),
    "Lethal contact did not report both the confirmed hit and player death.");

var dyingBird = new Enemy(5, Vector2.Zero, 2);
dyingBird.TakeDamage(75);
var lethalProjectileSystem = new ProjectileSystem(emptyTerrain);
lethalProjectileSystem.Spawn(NewProjectile(new Vector2(-1f, 0f), Vector2.UnitX));
var lethalProjectile = lethalProjectileSystem.Step(
    0.1f, [new ProjectileTarget(dyingBird, Vector2.Zero, 0.2f)]);
Assert(lethalProjectile == new ProjectileStepResult(1, 1),
    "Lethal projectile damage did not report both the confirmed bird hit and bird death.");

var audioSink = new CountingAudioSink();
GameplayFeedbackAudioRouter.Route(new GameplayFeedbackEvents(2, 1, true, 3, 1), audioSink);
Assert((audioSink.ProjectileFired, audioSink.PlayerHit, audioSink.PlayerDeath,
        audioSink.EnemyHit, audioSink.EnemyDeath) == (2, 1, 1, 3, 1),
    "Confirmed feedback events were not routed one-for-one to audio cues.");
Console.WriteLine("PASS: contact gating and headless audio routing follow confirmed gameplay events one-for-one");

var deterministicFirst = SimulateSequence();
var deterministicSecond = SimulateSequence();
Assert(deterministicFirst.SequenceEqual(deterministicSecond), "Equivalent combat inputs produced different simulation state.");
Console.WriteLine("PASS: equivalent deterministic input produces equivalent projectile state");

var unlimitedState = new WeaponState();
for (var i = 0; i < 100; i++) Assert(weapon.Fire(unlimitedState, player, true) is not null, "Original unlimited weapon ran out of ammo.");
Assert(unlimitedState.ShotsFired == 100, "Unlimited fire count was incorrect.");
Console.WriteLine("PASS: original weapon has unlimited ammo and no reload behavior");

static Projectile NewProjectile(Vector2 position, Vector2 direction) =>
    new(position, direction, 20f, 25, 0.34f, 0f, 0f);

static (Vector2 Position, Vector2 Direction, float Age)[] SimulateSequence()
{
    var terrain = new TerrainCollisionSystem([]);
    var projectiles = new ProjectileSystem(terrain);
    var weapon = new WeaponSystem();
    var state = new WeaponState();
    var player = new Player(new Vector2(4f, 2f));
    foreach (var firePressed in new[] { true, false, false, true, false })
    {
        var shot = weapon.Fire(state, player, firePressed);
        if (shot is not null) projectiles.Spawn(shot);
        projectiles.Step(0.02f);
    }
    return projectiles.Projectiles.Select(projectile => (projectile.Position, projectile.Direction, projectile.Age)).ToArray();
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class DummyDamageable : IDamageable
{
    public int Health { get; private set; } = 100;
    public DamageResult TakeDamage(int damage)
    {
        Health -= damage;
        return new DamageResult(true, Health <= 0);
    }
}

sealed class CountingAudioSink : IGameplayAudioSink
{
    public int ProjectileFired { get; private set; }
    public int PlayerHit { get; private set; }
    public int PlayerDeath { get; private set; }
    public int EnemyHit { get; private set; }
    public int EnemyDeath { get; private set; }

    public void PlayProjectileFired() => ProjectileFired++;
    public void PlayPlayerHit() => PlayerHit++;
    public void PlayPlayerDeath() => PlayerDeath++;
    public void PlayEnemyHit() => EnemyHit++;
    public void PlayEnemyDeath() => EnemyDeath++;
}
