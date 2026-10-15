using System.Numerics;
using BirdHell.Core;
using BirdHell.Entities;
using BirdHell.Presentation;
using BirdHell.Systems;
using BirdHell.World;
using Microsoft.Xna.Framework.Input;

StartupAndFocusAreDeterministic();
OptionsMapAndPersist();
PlayPauseAndResumeTransitions();
PauseMenuActionsAndFocus();
PausedRunIsFrozenAndPreserved();
PauseRestartAndNewCaveSemantics();
ReturnToMainMenuRetainsOptions();
ConfiguredRunsHonorGameplayOptions();
InvincibilityPreventsDamageAndFeedback();
ExitUsesFlowRequest();
KeyboardInputIsEdgeTriggered();
ControllerInputIsEdgeTriggered();
KeyboardAndControllerActionsAreEquivalent();
PauseControllerMappingIsEdgeTriggered();
PauseRenderDimensionsAreSafe();
PulseIsBoundedAndDeterministic();

Console.WriteLine("PASS: menu, pause flow/freezing/reset, options, controller mapping, rendering policy, and pulse checks");

static void StartupAndFocusAreDeterministic()
{
    var flow = new GameFlow();
    Assert(flow.Screen == GameScreen.MainMenu, "Startup state was not Main Menu.");
    Assert(flow.FocusedMainMenuItem == MainMenuItem.Play, "Play was not the default focus.");
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedMainMenuItem == MainMenuItem.Options, "Down did not focus Options.");
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedMainMenuItem == MainMenuItem.Exit, "Second Down did not focus Exit.");
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedMainMenuItem == MainMenuItem.Play, "Main-menu focus did not deliberately wrap.");
    flow.Handle(MenuAction.Previous);
    Assert(flow.FocusedMainMenuItem == MainMenuItem.Exit, "Reverse main-menu wrapping was not deterministic.");
}

static void OptionsMapAndPersist()
{
    var flow = OpenOptions();
    Assert(flow.FocusedOptionsMenuItem == OptionsMenuItem.BirdCount, "Options did not focus Bird Count.");
    Assert(flow.Options.BirdCount == 15 && !flow.Options.Invincible && !flow.Options.Fullscreen,
        "Default gameplay options changed.");
    Assert(flow.Options.CaveProfile.Profile == CaveGenerationProfile.Baseline,
        "Standard did not map to the baseline profile.");
    flow.Options.SetBirdCount(GameOptions.MaximumBirdCount);
    flow.Handle(MenuAction.Increase);
    Assert(flow.Options.BirdCount == 50, "Bird Count exceeded the normal UI maximum.");
    flow.Options.SetBirdCount(GameOptions.MinimumBirdCount);
    flow.Handle(MenuAction.Decrease);
    Assert(flow.Options.BirdCount == 1, "Bird Count dropped below the normal UI minimum.");
    flow.Options.SetBirdCount(GameOptions.DefaultBirdCount);

    flow.Handle(MenuAction.Increase);
    Assert(flow.Options.BirdCount == 16, "Bird Count did not increase.");
    flow.Handle(MenuAction.Next);
    flow.Handle(MenuAction.Increase);
    Assert(flow.Options.CaveProfile.Profile == CaveGenerationProfile.Complex,
        "Complex did not map to the existing complex profile.");
    flow.Handle(MenuAction.Increase);
    Assert(flow.Options.CaveProfile.Profile == CaveGenerationProfile.Extreme,
        "Extreme did not map to the existing extreme profile.");
    flow.Handle(MenuAction.Increase);
    Assert(flow.Options.CaveProfile.Profile == CaveGenerationProfile.Baseline,
        "Cave profile choices did not wrap back to Standard/baseline.");
    flow.Handle(MenuAction.Next);
    flow.Handle(MenuAction.Activate);
    Assert(flow.Options.Invincible, "Invincibility did not toggle on.");
    flow.Handle(MenuAction.Next);
    var display = flow.Handle(MenuAction.Activate);
    Assert(flow.Options.Fullscreen && display.ApplyDisplayMode,
        "Display Mode did not request a runtime fullscreen application.");
    flow.Handle(MenuAction.Next);
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedOptionsMenuItem == OptionsMenuItem.BirdCount,
        "Options focus did not deliberately wrap.");
    var back = flow.Handle(MenuAction.Back);
    Assert(flow.Screen == GameScreen.MainMenu && !back.EndGameplay,
        "Options Back did not return to Main Menu.");
    Assert(flow.Options.BirdCount == 16 && flow.Options.Invincible && flow.Options.Fullscreen,
        "Settings did not survive Options to Main Menu.");
    flow.Handle(MenuAction.Next);
    flow.Handle(MenuAction.Activate);
    Assert(flow.Options.BirdCount == 16 && flow.Options.Invincible && flow.Options.Fullscreen,
        "Settings did not survive Main Menu to Options.");
}

static void PlayPauseAndResumeTransitions()
{
    var flow = new GameFlow();
    var play = flow.Handle(MenuAction.Activate);
    Assert(play.StartGameplay && flow.Screen == GameScreen.Gameplay,
        "Play did not request and enter gameplay.");
    var pause = flow.Handle(MenuAction.Escape);
    Assert(pause.PauseGameplay && !pause.EndGameplay && flow.Screen == GameScreen.Paused,
        "Gameplay Escape did not enter a real Pause state.");
    Assert(flow.FocusedPauseMenuItem == PauseMenuItem.Resume && !flow.GameplayCanAdvance,
        "Pause did not default focus to Resume or continued allowing simulation.");
    var resume = flow.Handle(MenuAction.Escape);
    Assert(resume.ResumeGameplay && flow.Screen == GameScreen.Gameplay && flow.GameplayCanAdvance,
        "Paused Escape did not resume gameplay.");
}

static void PauseMenuActionsAndFocus()
{
    var flow = PausedFlow();
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedPauseMenuItem == PauseMenuItem.Restart, "Pause Down did not focus Restart.");
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedPauseMenuItem == PauseMenuItem.NewCave, "Pause Down did not focus New Cave.");
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedPauseMenuItem == PauseMenuItem.ReturnToMainMenu,
        "Pause Down did not focus Return to Main Menu.");
    flow.Handle(MenuAction.Next);
    Assert(flow.FocusedPauseMenuItem == PauseMenuItem.Resume, "Pause focus did not wrap to Resume.");
    flow.Handle(MenuAction.Previous);
    Assert(flow.FocusedPauseMenuItem == PauseMenuItem.ReturnToMainMenu,
        "Reverse pause focus did not wrap deterministically.");

    var resumeFlow = PausedFlow();
    var resume = resumeFlow.Handle(MenuAction.Activate);
    Assert(resume.ResumeGameplay && !resume.RestartGameplay && !resume.StartNewCave,
        "Resume requested a reset or regeneration.");
}

static void PausedRunIsFrozenAndPreserved()
{
    var flow = PausedFlow();
    var run = new MainScreenRun("pause-frozen-0001");
    run.PlayerMovement.Step(run.Player, new PlayerInput(0f, true, true), 0.02f);
    run.PlayerMovement.Step(run.Player, new PlayerInput(0f, false, false), 0.02f);
    run.PlayerMovement.Step(run.Player, new PlayerInput(0f, true, true), 0.02f);
    Assert(run.Player.SecondJumpActive, "Pause test setup did not activate the second jump.");
    run.Player.TakeDamage(25);
    run.Enemies.Enemies[0].TakeDamage(25);
    run.Projectiles.Spawn(new Projectile(Vector2.Zero, Vector2.UnitX, 20f, 25, 0.34f, 0.34f, 0.28f));

    var player = run.Player;
    var enemy = run.Enemies.Enemies[0];
    var projectile = run.Projectiles.Projectiles[0];
    var seed = run.Seed;
    var playerPosition = player.Position;
    var playerVelocity = player.Velocity;
    var playerHealth = player.Health;
    var playerFeedback = player.DamageFlashTime;
    var secondJumpElapsed = player.SecondJumpElapsed;
    var enemyPosition = enemy.Position;
    var enemyVelocity = enemy.Velocity;
    var enemyHealth = enemy.Health;
    var enemyFeedback = enemy.DamageFlashTime;
    var enemyContact = enemy.ContactTimer;
    var projectilePosition = projectile.Position;
    var projectileAge = projectile.Age;

    for (var index = 0; index < 20; index++)
        if (flow.GameplayCanAdvance)
            run.Step(new PlayerInput(1f, true, true), true, 0.02f);

    Assert(ReferenceEquals(run.Player, player) && run.Seed == seed,
        "Pause replaced the active run or changed its seed.");
    Assert(player.Position == playerPosition && player.Velocity == playerVelocity && player.Health == playerHealth,
        "Paused player movement or health advanced.");
    Assert(player.DamageFlashTime == playerFeedback, "Paused player feedback timer advanced.");
    Assert(player.SecondJumpActive && player.SecondJumpElapsed == secondJumpElapsed,
        "Paused gameplay advanced or cleared the active second-jump timer.");
    Assert(enemy.Position == enemyPosition && enemy.Velocity == enemyVelocity && enemy.Health == enemyHealth &&
           enemy.ContactTimer == enemyContact,
        "Paused enemy navigation/combat state advanced.");
    Assert(enemy.DamageFlashTime == enemyFeedback, "Paused enemy feedback timer advanced.");
    Assert(ReferenceEquals(run.Projectiles.Projectiles[0], projectile) &&
           projectile.Position == projectilePosition && projectile.Age == projectileAge,
        "Paused projectile state advanced.");

    var resume = flow.Handle(MenuAction.TogglePause);
    Assert(resume.ResumeGameplay && ReferenceEquals(run.Player, player) && run.Seed == seed,
        "Resume did not preserve the exact suspended run.");
    run.Step(new PlayerInput(0f, false, false), false, 0.02f);
    Assert(player.SecondJumpElapsed > secondJumpElapsed,
        "Resumed gameplay did not continue the second-jump timer from its frozen value.");
}

static void PauseRestartAndNewCaveSemantics()
{
    var options = new GameOptions();
    options.SetBirdCount(7);
    options.CycleCaveProfile(1);
    options.ToggleInvincibility();
    options.ToggleFullscreen();
    var run = new MainScreenRun("pause-reset-0001", options.CaveProfile, options.BirdCount, options.Invincible);
    var originalSeed = run.Seed;
    var originalPlayer = run.Player;
    run.Player.TakeDamage(25);
    run.Projectiles.Spawn(new Projectile(Vector2.Zero, Vector2.UnitX, 20f, 25, 0.34f, 0.34f, 0.28f));

    var restartFlow = PausedFlow(options);
    restartFlow.Focus((int)PauseMenuItem.Restart);
    var restart = restartFlow.Handle(MenuAction.Activate);
    Assert(restart.RestartGameplay && restart.ResumeGameplay && restartFlow.Screen == GameScreen.Gameplay,
        "Restart did not return directly to Gameplay with a restart request.");
    run.RestartSameSeed();
    Assert(run.Seed == originalSeed && !ReferenceEquals(run.Player, originalPlayer),
        "Pause Restart did not use same-seed atomic replacement semantics.");
    Assert(run.Player.Health == 100 && run.Projectiles.Projectiles.Count == 0 && run.WeaponState.ShotsFired == 0,
        "Pause Restart retained transient player/projectile/weapon state.");
    Assert(run.CaveProfile == options.CaveProfile && run.Enemies.Enemies.Count == options.BirdCount &&
           run.Player.IsInvincible && options.Fullscreen,
        "Pause Restart did not retain active gameplay/display options.");

    var newFlow = PausedFlow(options);
    newFlow.Focus((int)PauseMenuItem.NewCave);
    var fresh = newFlow.Handle(MenuAction.Activate);
    Assert(fresh.StartNewCave && fresh.ResumeGameplay && newFlow.Screen == GameScreen.Gameplay,
        "New Cave did not return directly to Gameplay with a fresh-seed request.");
    run.StartNewRun();
    Assert(run.Seed != originalSeed && run.CaveProfile == options.CaveProfile &&
           run.Enemies.Enemies.Count == options.BirdCount && run.Player.IsInvincible && options.Fullscreen,
        "New Cave changed active options or failed to change the seed.");
}

static void ReturnToMainMenuRetainsOptions()
{
    var options = new GameOptions();
    options.SetBirdCount(9);
    options.ToggleInvincibility();
    options.ToggleFullscreen();
    var flow = PausedFlow(options);
    flow.Focus((int)PauseMenuItem.ReturnToMainMenu);
    var result = flow.Handle(MenuAction.Activate);
    Assert(result.EndGameplay && !result.ResumeGameplay && flow.Screen == GameScreen.MainMenu,
        "Return to Main Menu did not end the active run.");
    Assert(flow.FocusedMainMenuItem == MainMenuItem.Play && flow.Options.BirdCount == 9 &&
           flow.Options.Invincible && flow.Options.Fullscreen,
        "Return to Main Menu did not retain application-lifetime options.");
}

static void ConfiguredRunsHonorGameplayOptions()
{
    const string seed = "m10-smoke-0000";
    var defaultRun = new MainScreenRun(seed);
    Assert(defaultRun.Enemies.Enemies.Count == 15, "Default Play run did not create exactly 15 birds.");

    var configuredRun = new MainScreenRun(seed, CaveGenerationProfiles.Default, 7, true);
    Assert(configuredRun.Enemies.Enemies.Count == 7 && configuredRun.Player.IsInvincible,
        "Configured Bird Count or invincibility was not honored.");
    configuredRun.RestartSameSeed();
    Assert(configuredRun.Enemies.Enemies.Count == 7, "Configured Bird Count was not retained on reset.");
}

static void InvincibilityPreventsDamageAndFeedback()
{
    var player = new Player(Vector2.Zero, isInvincible: true);
    var enemy = new Enemy(1, player.ColliderCenter - Enemy.OriginalColliderOffset, 2)
    {
        ContactTimer = EnemyCombatSystem.ContactDelay,
    };
    var combat = new EnemyCombatSystem();
    var lastResult = default(EnemyCombatStepResult);
    for (var index = 0; index < 8; index++)
    {
        enemy.ContactTimer = EnemyCombatSystem.ContactDelay;
        lastResult = combat.Step([enemy], player, 0.02f);
        Assert(lastResult == default, "Prevented damage emitted a damage or death event.");
    }
    Assert(player.Health == player.MaximumHealth && player.IsAlive,
        "Invincibility allowed health loss or death.");
    Assert(player.DamageFlashTime == 0f, "Prevented damage triggered the red damage flash.");

    var sink = new CountingAudioSink();
    GameplayFeedbackAudioRouter.Route(
        new GameplayFeedbackEvents(0, lastResult.PlayerDamageEvents, lastResult.PlayerDied, 0, 0), sink);
    Assert(sink.PlayerHits == 0 && sink.PlayerDeaths == 0,
        "Prevented damage routed player-hit or death audio.");
}

static void ExitUsesFlowRequest()
{
    var flow = new GameFlow();
    flow.Handle(MenuAction.Previous);
    var exit = flow.Handle(MenuAction.Activate);
    Assert(exit.ExitRequested && flow.ExitRequested,
        "Exit did not request clean application shutdown through game-state logic.");
}

static void KeyboardInputIsEdgeTriggered()
{
    var input = new MenuInputAdapter();
    input.UpdateFrame(default, []);
    Assert(input.UpdateFrame(new KeyboardState(Keys.Down), []) == MenuAction.Next,
        "Keyboard Down did not produce Next.");
    Assert(input.UpdateFrame(new KeyboardState(Keys.Down), []) == MenuAction.None,
        "Held keyboard Down repeated.");
    input.UpdateFrame(default, []);
    Assert(input.UpdateFrame(new KeyboardState(Keys.Enter), []) == MenuAction.Activate,
        "Keyboard Enter did not activate.");
    Assert(input.UpdateFrame(new KeyboardState(Keys.Enter), []) == MenuAction.None,
        "Held keyboard Enter repeated.");
}

static void ControllerInputIsEdgeTriggered()
{
    var input = new MenuInputAdapter();
    input.UpdateFrame(default, [Pad()]);
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.DPadDown)]) == MenuAction.Next,
        "Controller D-pad Down did not produce Next.");
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.DPadDown)]) == MenuAction.None,
        "Held controller direction repeated.");
    input.UpdateFrame(default, [Pad()]);
    Assert(input.UpdateFrame(default, [Pad(y: -0.8f)]) == MenuAction.Next,
        "Left stick Down did not produce Next.");
    Assert(input.UpdateFrame(default, [Pad(y: -0.8f)]) == MenuAction.None,
        "Held left stick repeated.");
    input.UpdateFrame(default, [Pad()]);
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.A)]) == MenuAction.Activate,
        "Controller A did not activate.");
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.A)]) == MenuAction.None,
        "Held controller A repeated.");
}

static void KeyboardAndControllerActionsAreEquivalent()
{
    var keyboard = new MenuInputAdapter();
    var controller = new MenuInputAdapter();
    keyboard.UpdateFrame(default, []);
    controller.UpdateFrame(default, [Pad()]);
    var pairs = new[]
    {
        (Keys.Up, Buttons.DPadUp, MenuAction.Previous),
        (Keys.Down, Buttons.DPadDown, MenuAction.Next),
        (Keys.Left, Buttons.DPadLeft, MenuAction.Decrease),
        (Keys.Right, Buttons.DPadRight, MenuAction.Increase),
        (Keys.Space, Buttons.A, MenuAction.Activate),
    };
    foreach (var (key, button, expected) in pairs)
    {
        Assert(keyboard.UpdateFrame(new KeyboardState(key), []) == expected,
            $"Keyboard {key} did not produce {expected}.");
        Assert(controller.UpdateFrame(default, [Pad(buttons: button)]) == expected,
            $"Controller {button} did not produce {expected}.");
        keyboard.UpdateFrame(default, []);
        controller.UpdateFrame(default, [Pad()]);
    }
}

static void PauseControllerMappingIsEdgeTriggered()
{
    var input = new MenuInputAdapter();
    input.UpdateFrame(default, [Pad()]);
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.Start)]) == MenuAction.TogglePause,
        "Controller Start/Menu did not request pause.");
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.Start)]) == MenuAction.None,
        "Held Start/Menu repeated and could immediately activate Resume.");
    input.UpdateFrame(default, [Pad()]);
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.Back)]) == MenuAction.Back,
        "Controller Select/View did not produce Back.");
    input.UpdateFrame(default, [Pad()]);
    Assert(input.UpdateFrame(default, [Pad(buttons: Buttons.B)]) == MenuAction.None,
        "Controller B still performed global Back/Pause.");
    input.UpdateFrame(default, [Pad()]);
    Assert(input.UpdateFrame(new KeyboardState(Keys.Escape), [Pad()]) == MenuAction.Escape,
        "Keyboard Escape did not produce its pause/back action.");

    var flow = new GameFlow();
    flow.Handle(MenuAction.Activate);
    flow.Handle(MenuAction.Back);
    Assert(flow.Screen == GameScreen.Gameplay,
        "Controller Select/View incorrectly paused gameplay.");
    flow.Handle(MenuAction.TogglePause);
    Assert(flow.Screen == GameScreen.Paused, "Controller Start/Menu action did not pause gameplay.");
    flow.Handle(MenuAction.Back);
    Assert(flow.Screen == GameScreen.Paused,
        "Controller Select/View incorrectly left Pause or returned to Main Menu.");
    flow.Handle(MenuAction.None);
    Assert(flow.Screen == GameScreen.Paused && flow.FocusedPauseMenuItem == PauseMenuItem.Resume,
        "Opening pause immediately resumed or changed focus.");
    flow.Handle(MenuAction.TogglePause);
    Assert(flow.Screen == GameScreen.Gameplay, "Controller Start/Menu did not resume paused gameplay.");

    var options = OpenOptions();
    options.Handle(MenuAction.Back);
    Assert(options.Screen == GameScreen.MainMenu, "Select/View Back semantics did not leave Options.");
}

static void PauseRenderDimensionsAreSafe()
{
    var dimensions = FrostedFrameDimensions.Calculate(1280, 720);
    Assert(dimensions.FirstPassWidth == 320 && dimensions.FirstPassHeight == 180 &&
           dimensions.SecondPassWidth == 160 && dimensions.SecondPassHeight == 90,
        "Frosted-frame downsample dimensions changed unexpectedly.");
    var minimum = FrostedFrameDimensions.Calculate(1, 1);
    Assert(minimum.FirstPassWidth == 1 && minimum.FirstPassHeight == 1 &&
           minimum.SecondPassWidth == 1 && minimum.SecondPassHeight == 1,
        "Frosted-frame dimensions were unsafe for a minimal resize.");
}

static void PulseIsBoundedAndDeterministic()
{
    var samples = Enumerable.Range(0, 200)
        .Select(index => MenuPulse.Value(index * MenuPulse.CycleSeconds / 199d))
        .ToArray();
    Assert(samples.All(value => value is >= 0f and <= 1f), "Menu glow pulse escaped its bounds.");
    Assert(MathF.Abs(MenuPulse.Value(0d) - MenuPulse.Value(MenuPulse.CycleSeconds)) < 0.00001f,
        "Menu glow pulse did not repeat deterministically.");
}

static GameFlow OpenOptions()
{
    var flow = new GameFlow();
    flow.Handle(MenuAction.Next);
    flow.Handle(MenuAction.Activate);
    Assert(flow.Screen == GameScreen.Options, "Options did not open.");
    return flow;
}

static GameFlow PausedFlow(GameOptions? options = null)
{
    var flow = new GameFlow(options);
    flow.Handle(MenuAction.Activate);
    flow.Handle(MenuAction.TogglePause);
    Assert(flow.Screen == GameScreen.Paused, "Test setup did not enter Pause.");
    return flow;
}

static GamePadState Pad(float y = 0f, Buttons buttons = Buttons.None) => new(
    new Microsoft.Xna.Framework.Vector2(0f, y), Microsoft.Xna.Framework.Vector2.Zero, 0f, 0f, buttons);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class CountingAudioSink : IGameplayAudioSink
{
    public int PlayerHits { get; private set; }
    public int PlayerDeaths { get; private set; }
    public void PlayProjectileFired() { }
    public void PlayPlayerHit() => PlayerHits++;
    public void PlayPlayerDeath() => PlayerDeaths++;
    public void PlayEnemyHit() { }
    public void PlayEnemyDeath() { }
}
