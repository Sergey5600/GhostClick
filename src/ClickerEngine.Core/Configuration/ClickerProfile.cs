namespace ClickerEngine.Core.Configuration;

/// <summary>
/// The complete, serializable description of one clicker configuration. This is the public
/// contract between the engine and whatever UI drives it - a plain POCO with no framework
/// types, so it round-trips through JSON (or an IPC boundary) unchanged.
/// </summary>
public sealed class ClickerProfile
{
    /// <summary>Schema version, so future readers can migrate old files.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Profile name; also the file name used by the JSON profile store.</summary>
    public string Name { get; set; } = "Default";

    /// <summary>Toggle (interval) or hold-to-click.</summary>
    public ClickMode Mode { get; set; } = ClickMode.Toggle;

    public ActionSettings Action { get; set; } = new();

    public IntervalSettings Interval { get; set; } = new();

    public RepeatSettings Repeat { get; set; } = new();

    public TargetSettings Target { get; set; } = new();

    public MovementSettings Movement { get; set; } = new();

    public JitterSettings Jitter { get; set; } = new();

    public DirectionalSettings Directional { get; set; } = new();

    public HotkeySettings Hotkeys { get; set; } = new();

    public SafetySettings Safety { get; set; } = new();

    /// <summary>A deep copy, so a UI can edit a draft without disturbing a running engine.</summary>
    public ClickerProfile Clone() => new()
    {
        Version = Version,
        Name = Name,
        Mode = Mode,
        Action = Action.Clone(),
        Interval = Interval.Clone(),
        Repeat = Repeat.Clone(),
        Target = Target.Clone(),
        Movement = Movement.Clone(),
        Jitter = Jitter.Clone(),
        Directional = Directional.Clone(),
        Hotkeys = Hotkeys.Clone(),
        Safety = Safety.Clone(),
    };

    /// <summary>The stage-1 defaults: F6 toggles 100 ms left clicks at the cursor.</summary>
    public static ClickerProfile CreateDefault(string name = "Default") => new() { Name = name };
}
