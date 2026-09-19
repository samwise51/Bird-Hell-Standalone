namespace BirdHell.Presentation;

/// <summary>User-selected debug presentation preferences, intentionally independent of run state.</summary>
public sealed class DebugPresentationSettings
{
    public bool ShowOutlines { get; private set; }
    public bool ShowCollision { get; private set; }
    public bool ShowNavigation { get; private set; }

    public void ToggleOutlines() => ShowOutlines = !ShowOutlines;
    public void ToggleCollision() => ShowCollision = !ShowCollision;
    public void ToggleNavigation() => ShowNavigation = !ShowNavigation;
}
