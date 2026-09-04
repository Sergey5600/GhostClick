namespace ClickerEngine.Core.Configuration;

/// <summary>What the engine emits on every action tick: which button or which key.</summary>
public sealed class ActionSettings
{
    /// <summary>Mouse clicks or keyboard presses.</summary>
    public ActionKind Kind { get; set; } = ActionKind.Mouse;

    /// <summary>Which mouse button to click when <see cref="Kind"/> is <see cref="ActionKind.Mouse"/>.</summary>
    public MouseButton Button { get; set; } = MouseButton.Left;

    /// <summary>Single or double click.</summary>
    public ClickType ClickType { get; set; } = ClickType.Single;

    /// <summary>Gap between the two clicks of a double click, in milliseconds.</summary>
    public int DoubleClickGapMs { get; set; } = 40;

    /// <summary>
    /// How long the button or key is held down, in milliseconds. Zero sends press and release
    /// back to back; a small non-zero value (20-60 ms) looks more like a human.
    /// </summary>
    public int HoldDurationMs { get; set; }

    /// <summary>Key pressed when <see cref="Kind"/> is <see cref="ActionKind.Keyboard"/>.</summary>
    public VirtualKey Key { get; set; } = VirtualKey.Space;

    /// <summary>Modifiers held around the key press.</summary>
    public KeyModifiers KeyModifiers { get; set; } = KeyModifiers.None;

    public ActionSettings Clone() => (ActionSettings)MemberwiseClone();
}

/// <summary>Delay between consecutive actions, with optional randomization.</summary>
public sealed class IntervalSettings
{
    /// <summary>Base delay between actions in milliseconds. Resolution is 1 ms.</summary>
    public int DelayMs { get; set; } = 100;

    /// <summary>When true, each delay is drawn uniformly from [<see cref="MinDelayMs"/>, <see cref="MaxDelayMs"/>].</summary>
    public bool RandomizeEnabled { get; set; }

    /// <summary>Lower bound of the randomized delay range, in milliseconds.</summary>
    public int MinDelayMs { get; set; } = 80;

    /// <summary>Upper bound of the randomized delay range, in milliseconds.</summary>
    public int MaxDelayMs { get; set; } = 140;

    public IntervalSettings Clone() => (IntervalSettings)MemberwiseClone();
}

/// <summary>How many actions a run performs before it stops on its own.</summary>
public sealed class RepeatSettings
{
    /// <summary>Infinite (until a hotkey or failsafe stops it) or a fixed count.</summary>
    public RepeatMode Mode { get; set; } = RepeatMode.Infinite;

    /// <summary>
    /// Number of actions to perform when <see cref="Mode"/> is <see cref="RepeatMode.FixedCount"/>.
    /// A double click counts as one action.
    /// </summary>
    public int Count { get; set; } = 100;

    public RepeatSettings Clone() => (RepeatSettings)MemberwiseClone();
}

/// <summary>One waypoint of a macro route.</summary>
public sealed class ClickPoint
{
    public ClickPoint()
    {
    }

    public ClickPoint(int x, int y, int delayMs = 0, string? label = null)
    {
        X = x;
        Y = y;
        DelayMs = delayMs;
        Label = label;
    }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>
    /// Dwell time after clicking this point, in milliseconds. Zero falls back to the
    /// profile-wide interval.
    /// </summary>
    public int DelayMs { get; set; }

    /// <summary>Optional free-text label, purely for the UI.</summary>
    public string? Label { get; set; }

    /// <summary>Whether the route walker visits this point.</summary>
    public bool Enabled { get; set; } = true;

    public ScreenPoint ToScreenPoint() => new(X, Y);

    public ClickPoint Clone() => new(X, Y, DelayMs, Label) { Enabled = Enabled };

    public override string ToString() =>
        Label is { Length: > 0 } ? $"{Label} ({X}, {Y})" : $"({X}, {Y})";
}

/// <summary>Where clicks land.</summary>
public sealed class TargetSettings
{
    public TargetMode Mode { get; set; } = TargetMode.CurrentCursor;

    /// <summary>Target used when <see cref="Mode"/> is <see cref="TargetMode.FixedPoint"/>.</summary>
    public ClickPoint FixedPoint { get; set; } = new();

    /// <summary>Waypoints walked when <see cref="Mode"/> is <see cref="TargetMode.MacroRoute"/>.</summary>
    public List<ClickPoint> Route { get; set; } = new();

    /// <summary>
    /// When true the cursor is returned to where it was before the run started, once
    /// the run finishes.
    /// </summary>
    public bool RestoreCursorOnStop { get; set; } = true;

    public TargetSettings Clone() => new()
    {
        Mode = Mode,
        FixedPoint = FixedPoint.Clone(),
        Route = Route.Select(p => p.Clone()).ToList(),
        RestoreCursorOnStop = RestoreCursorOnStop,
    };
}

/// <summary>How the cursor travels to a target.</summary>
public sealed class MovementSettings
{
    public MovementMode Mode { get; set; } = MovementMode.Instant;

    public EasingMode Easing { get; set; } = EasingMode.EaseInOut;

    /// <summary>
    /// Fixed travel time in milliseconds. When zero the duration is derived from
    /// <see cref="SpeedPixelsPerSecond"/> and the distance.
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>Travel speed used when <see cref="DurationMs"/> is zero.</summary>
    public double SpeedPixelsPerSecond { get; set; } = 1500;

    /// <summary>Time between interpolation samples, in milliseconds. Smaller is smoother.</summary>
    public int StepIntervalMs { get; set; } = 8;

    /// <summary>Hard cap on samples per move, so a slow long move cannot allocate without bound.</summary>
    public int MaxSteps { get; set; } = 2000;

    public MovementSettings Clone() => (MovementSettings)MemberwiseClone();
}

/// <summary>
/// Randomization applied to positions and timings so the emitted pattern is not perfectly
/// periodic. Note that this changes the shape of the input stream only - it cannot defeat
/// kernel-level anti-cheat (see README).
/// </summary>
public sealed class JitterSettings
{
    /// <summary>Enables the random target offset.</summary>
    public bool PositionJitterEnabled { get; set; }

    /// <summary>Maximum absolute horizontal offset applied to a click target, in pixels.</summary>
    public int MaxOffsetX { get; set; } = 3;

    /// <summary>Maximum absolute vertical offset applied to a click target, in pixels.</summary>
    public int MaxOffsetY { get; set; } = 3;

    /// <summary>Maximum wobble applied to intermediate points of a smooth move, in pixels.</summary>
    public int PathJitterPixels { get; set; }

    /// <summary>Enables the random scaling of every wait.</summary>
    public bool TimingJitterEnabled { get; set; }

    /// <summary>Wait times are scaled by 1 +/- this percentage.</summary>
    public double TimingJitterPercent { get; set; } = 10;

    public JitterSettings Clone() => (JitterSettings)MemberwiseClone();
}

/// <summary>Continuous drift along an angle, clicking as the cursor travels.</summary>
public sealed class DirectionalSettings
{
    /// <summary>
    /// Direction in degrees, 0-360. 0 is right (+X), 90 is up, 180 is left, 270 is down;
    /// i.e. the usual math convention, with screen Y inverted.
    /// </summary>
    public double AngleDegrees { get; set; }

    /// <summary>Drift speed in pixels per second.</summary>
    public double SpeedPixelsPerSecond { get; set; } = 200;

    /// <summary>Time between cursor updates while drifting, in milliseconds.</summary>
    public int StepIntervalMs { get; set; } = 10;

    /// <summary>
    /// When true the drift starts at <see cref="StartPoint"/>; otherwise it starts wherever
    /// the cursor is when the run begins.
    /// </summary>
    public bool UseStartPoint { get; set; }

    public ClickPoint StartPoint { get; set; } = new();

    /// <summary>What to do when the drift reaches the edge of the virtual desktop.</summary>
    public BoundsBehavior OnBounds { get; set; } = BoundsBehavior.Bounce;

    /// <summary>Random walk added to the angle on every click, in degrees. Zero keeps a straight line.</summary>
    public double AngleJitterDegrees { get; set; }

    public DirectionalSettings Clone() => new()
    {
        AngleDegrees = AngleDegrees,
        SpeedPixelsPerSecond = SpeedPixelsPerSecond,
        StepIntervalMs = StepIntervalMs,
        UseStartPoint = UseStartPoint,
        StartPoint = StartPoint.Clone(),
        OnBounds = OnBounds,
        AngleJitterDegrees = AngleJitterDegrees,
    };
}

/// <summary>The global hotkeys the engine listens for.</summary>
public sealed class HotkeySettings
{
    /// <summary>Toggles a run on and off (and in Hold mode, is the key that must stay down).</summary>
    public HotkeyBinding StartStop { get; set; } = new(VirtualKey.F6);

    /// <summary>Pauses and resumes a running session without resetting counters.</summary>
    public HotkeyBinding Pause { get; set; } = new(VirtualKey.F7);

    /// <summary>Always stops immediately, whatever the state.</summary>
    public HotkeyBinding EmergencyStop { get; set; } = new(VirtualKey.Escape);

    /// <summary>Captures the current cursor position into the route / fixed point.</summary>
    public HotkeyBinding RecordPoint { get; set; } = new(VirtualKey.F8);

    public HotkeySettings Clone() => new()
    {
        StartStop = StartStop.Clone(),
        Pause = Pause.Clone(),
        EmergencyStop = EmergencyStop.Clone(),
        RecordPoint = RecordPoint.Clone(),
    };
}

/// <summary>Limits that stop a runaway session.</summary>
public sealed class SafetySettings
{
    /// <summary>Enables the emergency-stop hotkey.</summary>
    public bool FailsafeEnabled { get; set; } = true;

    /// <summary>Maximum wall-clock run duration in seconds. Zero means unlimited.</summary>
    public int MaxRunDurationSeconds { get; set; } = 600;

    /// <summary>Maximum number of actions in a single run. Zero means unlimited.</summary>
    public int MaxActionsPerRun { get; set; }

    /// <summary>
    /// When true, a run also stops if the user physically moves the mouse away from the
    /// expected position while the engine is driving the cursor.
    /// </summary>
    public bool StopOnUserCursorMove { get; set; }

    /// <summary>How far the cursor may deviate before <see cref="StopOnUserCursorMove"/> fires, in pixels.</summary>
    public int UserCursorMoveThresholdPixels { get; set; } = 120;

    public SafetySettings Clone() => (SafetySettings)MemberwiseClone();
}
