namespace BirdHell.Core;

public enum GameScreen { MainMenu, Options, Gameplay, Paused }
public enum MainMenuItem { Play, Options, Exit }
public enum OptionsMenuItem { BirdCount, CaveProfile, Invincibility, DisplayMode, Back }
public enum PauseMenuItem { Resume, Restart, NewCave, ReturnToMainMenu }
public enum MenuAction { None, Previous, Next, Decrease, Increase, Activate, Back, Escape, TogglePause }

public readonly record struct GameFlowResult(
    bool StartGameplay = false,
    bool EndGameplay = false,
    bool PauseGameplay = false,
    bool ResumeGameplay = false,
    bool RestartGameplay = false,
    bool StartNewCave = false,
    bool ApplyDisplayMode = false,
    bool ExitRequested = false);

/// <summary>Renderer-independent application screen, focus, and option transition logic.</summary>
public sealed class GameFlow
{
    public GameFlow(GameOptions? options = null) => Options = options ?? new GameOptions();

    public GameScreen Screen { get; private set; } = GameScreen.MainMenu;
    public GameOptions Options { get; }
    public MainMenuItem FocusedMainMenuItem { get; private set; } = MainMenuItem.Play;
    public OptionsMenuItem FocusedOptionsMenuItem { get; private set; } = OptionsMenuItem.BirdCount;
    public PauseMenuItem FocusedPauseMenuItem { get; private set; } = PauseMenuItem.Resume;
    public int FocusedIndex => Screen switch
    {
        GameScreen.Options => (int)FocusedOptionsMenuItem,
        GameScreen.Paused => (int)FocusedPauseMenuItem,
        _ => (int)FocusedMainMenuItem,
    };
    public bool GameplayCanAdvance => Screen == GameScreen.Gameplay;
    public bool ExitRequested { get; private set; }

    public GameFlowResult Handle(MenuAction action)
    {
        if (action == MenuAction.None) return default;
        return Screen switch
        {
            GameScreen.MainMenu => HandleMainMenu(action),
            GameScreen.Options => HandleOptions(action),
            GameScreen.Gameplay when action is MenuAction.Escape or MenuAction.TogglePause => Pause(),
            GameScreen.Paused => HandlePause(action),
            _ => default,
        };
    }

    public void Focus(int index)
    {
        if (Screen == GameScreen.MainMenu)
            FocusedMainMenuItem = (MainMenuItem)Math.Clamp(index, 0, 2);
        else if (Screen == GameScreen.Options)
            FocusedOptionsMenuItem = (OptionsMenuItem)Math.Clamp(index, 0, 4);
        else if (Screen == GameScreen.Paused)
            FocusedPauseMenuItem = (PauseMenuItem)Math.Clamp(index, 0, 3);
    }

    private GameFlowResult HandleMainMenu(MenuAction action)
    {
        if (action is MenuAction.Previous or MenuAction.Next)
        {
            FocusedMainMenuItem = (MainMenuItem)Wrap(
                (int)FocusedMainMenuItem + (action == MenuAction.Previous ? -1 : 1), 3);
            return default;
        }
        if (action is not MenuAction.Activate) return default;
        switch (FocusedMainMenuItem)
        {
            case MainMenuItem.Play:
                Screen = GameScreen.Gameplay;
                return new GameFlowResult(StartGameplay: true);
            case MainMenuItem.Options:
                Screen = GameScreen.Options;
                FocusedOptionsMenuItem = OptionsMenuItem.BirdCount;
                return default;
            case MainMenuItem.Exit:
                ExitRequested = true;
                return new GameFlowResult(ExitRequested: true);
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private GameFlowResult HandleOptions(MenuAction action)
    {
        if (action is MenuAction.Back or MenuAction.Escape) return ReturnToMainMenu(false);
        if (action is MenuAction.Previous or MenuAction.Next)
        {
            FocusedOptionsMenuItem = (OptionsMenuItem)Wrap(
                (int)FocusedOptionsMenuItem + (action == MenuAction.Previous ? -1 : 1), 5);
            return default;
        }
        if (action == MenuAction.Activate && FocusedOptionsMenuItem == OptionsMenuItem.Back)
            return ReturnToMainMenu(false);
        if (action is not (MenuAction.Decrease or MenuAction.Increase or MenuAction.Activate)) return default;

        var direction = action == MenuAction.Decrease ? -1 : 1;
        switch (FocusedOptionsMenuItem)
        {
            case OptionsMenuItem.BirdCount:
                Options.AdjustBirdCount(direction);
                break;
            case OptionsMenuItem.CaveProfile:
                Options.CycleCaveProfile(direction);
                break;
            case OptionsMenuItem.Invincibility:
                Options.ToggleInvincibility();
                break;
            case OptionsMenuItem.DisplayMode:
                Options.ToggleFullscreen();
                return new GameFlowResult(ApplyDisplayMode: true);
        }
        return default;
    }

    private GameFlowResult HandlePause(MenuAction action)
    {
        if (action is MenuAction.Escape or MenuAction.TogglePause) return Resume();
        if (action is MenuAction.Previous or MenuAction.Next)
        {
            FocusedPauseMenuItem = (PauseMenuItem)Wrap(
                (int)FocusedPauseMenuItem + (action == MenuAction.Previous ? -1 : 1), 4);
            return default;
        }
        if (action != MenuAction.Activate) return default;

        return FocusedPauseMenuItem switch
        {
            PauseMenuItem.Resume => Resume(),
            PauseMenuItem.Restart => Resume(restartGameplay: true),
            PauseMenuItem.NewCave => Resume(startNewCave: true),
            PauseMenuItem.ReturnToMainMenu => ReturnToMainMenu(true),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private GameFlowResult Pause()
    {
        Screen = GameScreen.Paused;
        FocusedPauseMenuItem = PauseMenuItem.Resume;
        return new GameFlowResult(PauseGameplay: true);
    }

    private GameFlowResult Resume(bool restartGameplay = false, bool startNewCave = false)
    {
        Screen = GameScreen.Gameplay;
        return new GameFlowResult(
            ResumeGameplay: true,
            RestartGameplay: restartGameplay,
            StartNewCave: startNewCave);
    }

    private GameFlowResult ReturnToMainMenu(bool endedGameplay)
    {
        Screen = GameScreen.MainMenu;
        FocusedMainMenuItem = MainMenuItem.Play;
        return new GameFlowResult(EndGameplay: endedGameplay);
    }

    private static int Wrap(int value, int count) => (value % count + count) % count;
}
