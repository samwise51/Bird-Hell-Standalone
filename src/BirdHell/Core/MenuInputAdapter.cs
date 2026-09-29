using Microsoft.Xna.Framework.Input;

namespace BirdHell.Core;

/// <summary>Collapses keyboard and controller menu controls into one debounced logical action.</summary>
public sealed class MenuInputAdapter
{
    public const float StickThreshold = 0.55f;

    private KeyboardState _previousKeyboard;
    private GamePadState _previousGamePad;
    private int? _activeControllerIndex;
    private int _previousStickX;
    private int _previousStickY;
    private bool _initialized;

    public MenuAction UpdateFrame(KeyboardState keyboard, ReadOnlySpan<GamePadState> gamePads)
    {
        var previousIndex = _activeControllerIndex;
        if (_activeControllerIndex is { } active &&
            (active >= gamePads.Length || !gamePads[active].IsConnected))
            _activeControllerIndex = null;
        if (_activeControllerIndex is null)
            for (var index = 0; index < gamePads.Length; index++)
                if (gamePads[index].IsConnected)
                {
                    _activeControllerIndex = index;
                    break;
                }

        var controllerChanged = previousIndex != _activeControllerIndex;
        var gamePad = _activeControllerIndex is { } selected ? gamePads[selected] : default;
        var stickX = AxisDirection(gamePad.ThumbSticks.Left.X);
        var stickY = AxisDirection(gamePad.ThumbSticks.Left.Y);
        var action = MenuAction.None;
        if (_initialized)
        {
            if (Pressed(keyboard, Keys.Escape))
                action = MenuAction.Escape;
            else if (PadPressed(gamePad, Buttons.Back, controllerChanged))
                action = MenuAction.Back;
            else if (PadPressed(gamePad, Buttons.Start, controllerChanged))
                action = MenuAction.TogglePause;
            else if (Pressed(keyboard, Keys.Up) || PadPressed(gamePad, Buttons.DPadUp, controllerChanged) ||
                     stickY > 0 && _previousStickY <= 0)
                action = MenuAction.Previous;
            else if (Pressed(keyboard, Keys.Down) || PadPressed(gamePad, Buttons.DPadDown, controllerChanged) ||
                     stickY < 0 && _previousStickY >= 0)
                action = MenuAction.Next;
            else if (Pressed(keyboard, Keys.Left) || PadPressed(gamePad, Buttons.DPadLeft, controllerChanged) ||
                     stickX < 0 && _previousStickX >= 0)
                action = MenuAction.Decrease;
            else if (Pressed(keyboard, Keys.Right) || PadPressed(gamePad, Buttons.DPadRight, controllerChanged) ||
                     stickX > 0 && _previousStickX <= 0)
                action = MenuAction.Increase;
            else if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Space) ||
                     PadPressed(gamePad, Buttons.A, controllerChanged))
                action = MenuAction.Activate;
        }

        _previousKeyboard = keyboard;
        _previousGamePad = gamePad;
        _previousStickX = stickX;
        _previousStickY = stickY;
        _initialized = true;
        return action;
    }

    private bool Pressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    private bool PadPressed(GamePadState current, Buttons button, bool controllerChanged) =>
        current.IsConnected && !controllerChanged && current.IsButtonDown(button) && _previousGamePad.IsButtonUp(button);

    private static int AxisDirection(float value) => value >= StickThreshold ? 1 : value <= -StickThreshold ? -1 : 0;
}
