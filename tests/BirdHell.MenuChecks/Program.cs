using System.Numerics;
using BirdHell.Core;
using BirdHell.Entities;
using BirdHell.Presentation;
using BirdHell.Systems;
using BirdHell.World;
using Microsoft.Xna.Framework.Input;

StartupAndFocusAreDeterministic();
OptionsMapAndPersist();
PlayAndGameplayBackTransition();
ConfiguredRunsHonorGameplayOptions();
InvincibilityPreventsDamageAndFeedback();
ExitUsesFlowRequest();
KeyboardInputIsEdgeTriggered();
ControllerInputIsEdgeTriggered();
KeyboardAndControllerActionsAreEquivalent();
PulseIsBoundedAndDeterministic();

Console.WriteLine("PASS: menu state, options, gameplay creation, invincibility, input, and pulse checks");

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

static void PlayAndGameplayBackTransition()
{
    var flow = new GameFlow();
    var play = flow.Handle(MenuAction.Activate);
    Assert(play.StartGameplay && flow.Screen == GameScreen.Gameplay,
        "Play did not request and enter gameplay.");
    var back = flow.Handle(MenuAction.Back);
    Assert(back.EndGameplay && flow.Screen == GameScreen.MainMenu &&
           flow.FocusedMainMenuItem == MainMenuItem.Play,
        "Gameplay Escape/Back did not return to Main Menu focused on Play.");
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
        (Keys.Escape, Buttons.B, MenuAction.Back),
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
