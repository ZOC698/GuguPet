namespace GuguPet;

public sealed class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double PetWidth { get; set; } = 180;
    public double Opacity { get; set; } = 1;
    public double AnimationSpeed { get; set; } = 1;
    public bool GazeEnabled { get; set; } = true;
    public double GazeHoldSeconds { get; set; } = 1.5;
    public bool StayOnTop { get; set; } = true;
    public bool ReducedMotion { get; set; }
    public bool AutoIdleActions { get; set; } = true;
    public double IdleActionIntervalSeconds { get; set; } = 45;
    public bool AutoRoam { get; set; } = true;
    public double RoamSpeed { get; set; } = 72;
    public bool ChaseFastCursor { get; set; }
    public bool EdgeActionsEnabled { get; set; } = true;
    public bool CodexSyncEnabled { get; set; } = true;
    public bool ActivityBubbleEnabled { get; set; } = true;
    public double BubbleDisplaySeconds { get; set; } = 10;
    public bool StartWithWindows { get; set; }
    public bool StartWithCodex { get; set; }
    public bool StartupAnimationEnabled { get; set; } = true;
    public bool ShowControlPanelOnLaunch { get; set; }
    public string Language { get; set; } = "auto";
}
