using Microsoft.Xna.Framework.Input;

namespace BirdHell.Core;

public readonly record struct GameplayInputState(
    float Horizontal,
    bool JumpPressed,
    bool JumpHeld,
    bool FirePressed,
    bool CrouchHeld);

/// <summary>
/// Converts render-frame MonoGame device snapshots into buffered, device-neutral gameplay actions.
/// Pressed actions remain queued until the next fixed simulation step consumes them.
/// </summary>
public sealed class GameplayInputAdapter
{
    public const float LeftStickDeadZone = 0.20f;
    public const float TriggerPressThreshold = 0.55f;
    public const float TriggerReleaseThreshold = 0.45f;

    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;
    private GamePadState _previousGamePadState;
    private int? _activeControllerIndex;
    private bool _initialized;
    private bool _triggerEngaged;
    private bool _pendingJump;
    private bool _pendingFire;
    private float _horizontal;
    private bool _jumpHeld;
    private bool _crouchHeld;

    public int? ActiveControllerIndex => _activeControllerIndex;

    public void UpdateFrame(
        KeyboardState keyboardState,
        MouseState mouseState,
        ReadOnlySpan<GamePadState> gamePadStates)
    {
        var previousControllerIndex = _activeControllerIndex;
        if (_activeControllerIndex is { } activeIndex &&
            (activeIndex >= gamePadStates.Length || !gamePadStates[activeIndex].IsConnected))
            _activeControllerIndex = null;

        if (_activeControllerIndex is null)
            for (var index = 0; index < gamePadStates.Length; index++)
                if (gamePadStates[index].IsConnected)
                {
                    _activeControllerIndex = index;
                    break;
                }

        var controllerChanged = previousControllerIndex != _activeControllerIndex;
        var gamePadState = _activeControllerIndex is { } selectedIndex
            ? gamePadStates[selectedIndex]
            : default;

        var keyboardMovementHeld =
            keyboardState.IsKeyDown(Keys.A) || keyboardState.IsKeyDown(Keys.D) ||
            keyboardState.IsKeyDown(Keys.Left) || keyboardState.IsKeyDown(Keys.Right);
        if (keyboardMovementHeld)
        {
            _horizontal = 0f;
            if (keyboardState.IsKeyDown(Keys.A) || keyboardState.IsKeyDown(Keys.Left)) _horizontal -= 1f;
            if (keyboardState.IsKeyDown(Keys.D) || keyboardState.IsKeyDown(Keys.Right)) _horizontal += 1f;
        }
        else
        {
            _horizontal = gamePadState.IsConnected
                ? ApplyDeadZone(gamePadState.ThumbSticks.Left.X)
                : 0f;
        }

        var keyboardJumpHeld = keyboardState.IsKeyDown(Keys.Space);
        var controllerJumpHeld = gamePadState.IsConnected && gamePadState.IsButtonDown(Buttons.A);
        _jumpHeld = keyboardJumpHeld || controllerJumpHeld;
        _crouchHeld = keyboardState.IsKeyDown(Keys.S) || keyboardState.IsKeyDown(Keys.Down);

        if (_initialized)
        {
            _pendingJump |= WasPressed(keyboardState, _previousKeyboardState, Keys.Space);
            _pendingFire |= WasPressed(keyboardState, _previousKeyboardState, Keys.LeftControl) ||
                            mouseState.LeftButton == ButtonState.Pressed &&
                            _previousMouseState.LeftButton == ButtonState.Released;

            if (gamePadState.IsConnected && !controllerChanged)
            {
                _pendingJump |= gamePadState.IsButtonDown(Buttons.A) &&
                                _previousGamePadState.IsButtonUp(Buttons.A);
                UpdateTrigger(gamePadState.Triggers.Right);
            }
        }

        if (!_initialized || controllerChanged)
            _triggerEngaged = gamePadState.IsConnected &&
                                gamePadState.Triggers.Right > TriggerReleaseThreshold;

        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
        _previousGamePadState = gamePadState;
        _initialized = true;
    }

    public GameplayInputState ConsumeSimulationStep()
    {
        var result = new GameplayInputState(
            _horizontal,
            _pendingJump,
            _jumpHeld,
            _pendingFire,
            _crouchHeld);
        _pendingJump = false;
        _pendingFire = false;
        return result;
    }

    public void ClearBufferedEdges()
    {
        _pendingJump = false;
        _pendingFire = false;
    }

    public static float ApplyDeadZone(float value)
    {
        value = Math.Clamp(value, -1f, 1f);
        var magnitude = MathF.Abs(value);
        if (magnitude <= LeftStickDeadZone) return 0f;
        return MathF.CopySign((magnitude - LeftStickDeadZone) / (1f - LeftStickDeadZone), value);
    }

    private void UpdateTrigger(float value)
    {
        if (_triggerEngaged)
        {
            if (value <= TriggerReleaseThreshold) _triggerEngaged = false;
            return;
        }

        if (value < TriggerPressThreshold) return;
        _triggerEngaged = true;
        _pendingFire = true;
    }

    private static bool WasPressed(KeyboardState current, KeyboardState previous, Keys key) =>
        current.IsKeyDown(key) && previous.IsKeyUp(key);
}
