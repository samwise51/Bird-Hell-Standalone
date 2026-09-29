using BirdHell.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

KeyboardMovementRemainsDigital();
KeyboardAndMouseActionsRemainMapped();
StickDeadZoneAndAnalogMovementWork();
ControllerAControlsJumpOnly();
TriggerUsesCrossingAndHysteresis();
DevicesCollapseIntoLogicalActions();
PressedEdgesRemainBufferedUntilOneSimulationStep();
ControllerSelectionDisconnectAndReconnectAreSafe();

Console.WriteLine("PASS: gameplay input mapping, buffering, controller selection, and reconnect behavior");

static void KeyboardMovementRemainsDigital()
{
    var input = Initialized();

    input.UpdateFrame(Keyboard(Keys.D), default, []);
    AssertNear(input.ConsumeSimulationStep().Horizontal, 1f, "D did not move right.");
    input.UpdateFrame(Keyboard(Keys.A), default, []);
    AssertNear(input.ConsumeSimulationStep().Horizontal, -1f, "A did not move left.");
    input.UpdateFrame(Keyboard(Keys.Right), default, []);
    AssertNear(input.ConsumeSimulationStep().Horizontal, 1f, "Right arrow did not move right.");
    input.UpdateFrame(Keyboard(Keys.Left), default, []);
    AssertNear(input.ConsumeSimulationStep().Horizontal, -1f, "Left arrow did not move left.");
}

static void KeyboardAndMouseActionsRemainMapped()
{
    var jump = Initialized();
    jump.UpdateFrame(Keyboard(Keys.Space), default, []);
    var jumpStep = jump.ConsumeSimulationStep();
    Assert(jumpStep.JumpPressed && jumpStep.JumpHeld, "Space did not produce pressed and held jump actions.");

    var controlFire = Initialized();
    controlFire.UpdateFrame(Keyboard(Keys.LeftControl), default, []);
    Assert(controlFire.ConsumeSimulationStep().FirePressed, "Left Ctrl did not produce a fire edge.");

    var mouseFire = Initialized();
    mouseFire.UpdateFrame(default, Mouse(leftPressed: true), []);
    Assert(mouseFire.ConsumeSimulationStep().FirePressed, "Left mouse did not produce a fire edge.");
}

static void StickDeadZoneAndAnalogMovementWork()
{
    var input = Initialized(Pad());
    input.UpdateFrame(default, default, [Pad(x: GameplayInputAdapter.LeftStickDeadZone)]);
    AssertNear(input.ConsumeSimulationStep().Horizontal, 0f, "Dead-zone boundary was not neutral.");
    input.UpdateFrame(default, default, [Pad(x: 0.60f)]);
    Assert(input.ConsumeSimulationStep().Horizontal > 0f, "Positive stick input outside the dead zone was lost.");
    input.UpdateFrame(default, default, [Pad(x: -0.60f)]);
    Assert(input.ConsumeSimulationStep().Horizontal < 0f, "Negative stick input outside the dead zone was lost.");
}

static void ControllerAControlsJumpOnly()
{
    var input = Initialized(Pad());
    input.UpdateFrame(default, default, [Pad(buttons: Buttons.A)]);
    var first = input.ConsumeSimulationStep();
    Assert(first.JumpPressed && first.JumpHeld, "Controller A did not produce a jump edge and held state.");
    Assert(!first.FirePressed, "Controller A still produced a fire action.");

    input.UpdateFrame(default, default, [Pad(buttons: Buttons.A)]);
    var held = input.ConsumeSimulationStep();
    Assert(!held.JumpPressed && held.JumpHeld, "Holding controller A repeated its edge or lost JumpHeld.");
}

static void TriggerUsesCrossingAndHysteresis()
{
    var input = Initialized(Pad());
    input.UpdateFrame(default, default, [Pad(trigger: GameplayInputAdapter.TriggerPressThreshold)]);
    Assert(input.ConsumeSimulationStep().FirePressed, "Right trigger threshold crossing did not fire.");

    input.UpdateFrame(default, default, [Pad(trigger: 1f)]);
    Assert(!input.ConsumeSimulationStep().FirePressed, "Holding right trigger repeatedly fired.");

    foreach (var value in new[] { 0.54f, 0.46f, 0.56f, 0.50f })
    {
        input.UpdateFrame(default, default, [Pad(trigger: value)]);
        Assert(!input.ConsumeSimulationStep().FirePressed, "Trigger jitter inside the hysteresis band fired.");
    }

    input.UpdateFrame(default, default, [Pad(trigger: GameplayInputAdapter.TriggerReleaseThreshold)]);
    Assert(!input.ConsumeSimulationStep().FirePressed, "Trigger release generated a fire action.");
    input.UpdateFrame(default, default, [Pad(trigger: GameplayInputAdapter.TriggerPressThreshold)]);
    Assert(input.ConsumeSimulationStep().FirePressed, "Trigger did not re-arm after crossing the release threshold.");
}

static void DevicesCollapseIntoLogicalActions()
{
    var input = Initialized(Pad());
    input.UpdateFrame(
        Keyboard(Keys.A, Keys.Space, Keys.LeftControl),
        Mouse(leftPressed: true),
        [Pad(x: 0.9f, trigger: 1f, buttons: Buttons.A)]);
    var state = input.ConsumeSimulationStep();
    AssertNear(state.Horizontal, -1f, "Keyboard movement did not take precedence over the stick.");
    Assert(state.JumpPressed && state.JumpHeld && state.FirePressed,
        "Physical inputs did not collapse into logical jump and fire actions.");
}

static void PressedEdgesRemainBufferedUntilOneSimulationStep()
{
    var input = Initialized(Pad());
    input.UpdateFrame(Keyboard(Keys.Space, Keys.LeftControl), default, [Pad()]);
    input.UpdateFrame(Keyboard(Keys.Space, Keys.LeftControl), default, [Pad()]);

    var firstStep = input.ConsumeSimulationStep();
    Assert(firstStep.JumpPressed && firstStep.FirePressed,
        "Pressed edges did not survive render frames without a simulation step.");
    var catchUpStep = input.ConsumeSimulationStep();
    Assert(!catchUpStep.JumpPressed && !catchUpStep.FirePressed && catchUpStep.JumpHeld,
        "Pressed edges were not consumed only by the first catch-up step.");
}

static void ControllerSelectionDisconnectAndReconnectAreSafe()
{
    var input = Initialized(default, default);
    input.UpdateFrame(default, default, [default, Pad(x: 0.7f)]);
    Assert(input.ActiveControllerIndex == 1, "The first connected controller was not selected automatically.");
    Assert(input.ConsumeSimulationStep().Horizontal > 0f, "Selected controller movement was not used.");

    input.UpdateFrame(default, default, [Pad(x: -0.7f), Pad(x: 0.7f)]);
    Assert(input.ActiveControllerIndex == 1 && input.ConsumeSimulationStep().Horizontal > 0f,
        "The active controller was not retained while it remained connected.");

    input.UpdateFrame(default, default, [default, default]);
    var disconnected = input.ConsumeSimulationStep();
    Assert(input.ActiveControllerIndex is null && disconnected.Horizontal == 0f &&
           !disconnected.JumpPressed && !disconnected.JumpHeld && !disconnected.FirePressed,
        "Disconnect did not produce neutral controller input.");

    input.UpdateFrame(default, default, [Pad(trigger: 1f, buttons: Buttons.A), default]);
    var reconnected = input.ConsumeSimulationStep();
    Assert(input.ActiveControllerIndex == 0 && !reconnected.JumpPressed && !reconnected.FirePressed,
        "Reconnect with held controls generated a phantom action.");
    Assert(reconnected.JumpHeld, "A held during reconnect was not represented as a held action.");

    input.UpdateFrame(default, default, [Pad(trigger: 0f), default]);
    input.ConsumeSimulationStep();
    input.UpdateFrame(default, default, [Pad(trigger: 1f, buttons: Buttons.A), default]);
    var repressed = input.ConsumeSimulationStep();
    Assert(repressed.JumpPressed && repressed.JumpHeld && repressed.FirePressed,
        "Release and repress after reconnect did not restore normal actions.");
}

static GameplayInputAdapter Initialized(params GamePadState[] gamePads)
{
    var input = new GameplayInputAdapter();
    input.UpdateFrame(default, default, gamePads);
    input.ConsumeSimulationStep();
    return input;
}

static KeyboardState Keyboard(params Keys[] keys) => new(keys);

static MouseState Mouse(bool leftPressed) => new(
    0,
    0,
    0,
    leftPressed ? ButtonState.Pressed : ButtonState.Released,
    ButtonState.Released,
    ButtonState.Released,
    ButtonState.Released,
    ButtonState.Released);

static GamePadState Pad(float x = 0f, float trigger = 0f, Buttons buttons = Buttons.None) => new(
    new Vector2(x, 0f),
    Vector2.Zero,
    0f,
    trigger,
    buttons);

static void AssertNear(float actual, float expected, string message)
{
    if (MathF.Abs(actual - expected) > 0.00001f)
        throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
