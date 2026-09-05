namespace ClickerEngine.Core.Configuration;

/// <summary>What kind of input the engine emits on every action tick.</summary>
public enum ActionKind
{
    /// <summary>Mouse button clicks.</summary>
    Mouse = 0,

    /// <summary>Keyboard key presses.</summary>
    Keyboard = 1,
}

/// <summary>How a run is started and stopped.</summary>
public enum ClickMode
{
    /// <summary>The hotkey toggles the run on and off; actions repeat on an interval.</summary>
    Toggle = 0,

    /// <summary>Actions are emitted only while the trigger hotkey is physically held down.</summary>
    Hold = 1,
}

/// <summary>Physical mouse buttons that can be emitted or bound as a hotkey.</summary>
public enum MouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    XButton1 = 3,
    XButton2 = 4,
}

/// <summary>Single or double click per action tick.</summary>
public enum ClickType
{
    Single = 0,
    Double = 1,
}

/// <summary>Where the click is delivered.</summary>
public enum TargetMode
{
    /// <summary>Click wherever the cursor happens to be; the cursor is never moved.</summary>
    CurrentCursor = 0,

    /// <summary>Click at one fixed screen coordinate.</summary>
    FixedPoint = 1,

    /// <summary>Walk a recorded list of points in order, each with its own dwell time.</summary>
    MacroRoute = 2,

    /// <summary>Drift continuously along an angle, clicking as the cursor travels.</summary>
    Directional = 3,
}

/// <summary>How the cursor gets from its current position to the next target.</summary>
public enum MovementMode
{
    /// <summary>Teleport: a single absolute move event.</summary>
    Instant = 0,

    /// <summary>Interpolated over time in small steps.</summary>
    Smooth = 1,
}

/// <summary>Interpolation curve used by <see cref="MovementMode.Smooth"/>.</summary>
public enum EasingMode
{
    Linear = 0,
    EaseIn = 1,
    EaseOut = 2,
    EaseInOut = 3,
}

/// <summary>Keyboard modifiers, matching the Win32 MOD_* bit layout.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>Whether a run stops after a set number of actions.</summary>
public enum RepeatMode
{
    /// <summary>Repeat until stopped by a hotkey or a failsafe.</summary>
    Infinite = 0,

    /// <summary>Stop after <c>RepeatSettings.Count</c> actions.</summary>
    FixedCount = 1,
}

/// <summary>What happens when directional drift reaches the edge of the virtual desktop.</summary>
public enum BoundsBehavior
{
    /// <summary>Stop the run.</summary>
    Stop = 0,

    /// <summary>Reflect the direction vector off the edge and keep going.</summary>
    Bounce = 1,

    /// <summary>Re-enter from the opposite edge.</summary>
    Wrap = 2,

    /// <summary>Jump back to the start point and keep going.</summary>
    Restart = 3,
}
