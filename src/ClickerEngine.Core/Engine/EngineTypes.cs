using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Engine;

/// <summary>Lifecycle of a run.</summary>
public enum EngineState
{
    /// <summary>Nothing is running.</summary>
    Idle,

    /// <summary>A run is in progress.</summary>
    Running,

    /// <summary>A run is in progress but suspended at the next step boundary.</summary>
    Paused,

    /// <summary>A stop has been requested and the run loop is unwinding.</summary>
    Stopping,
}

/// <summary>Why a run ended.</summary>
public enum StopReason
{
    /// <summary>The user pressed the start/stop hotkey or called <c>Stop</c>.</summary>
    User,

    /// <summary>The planned number of actions was reached.</summary>
    Completed,

    /// <summary>The Hold-mode trigger key was released.</summary>
    HoldReleased,

    /// <summary>The emergency-stop hotkey fired.</summary>
    Failsafe,

    /// <summary>The configured maximum run duration elapsed.</summary>
    TimeLimit,

    /// <summary>The configured maximum action count was reached.</summary>
    ActionLimit,

    /// <summary>The cursor was moved away from where the engine put it.</summary>
    UserCursorMoved,

    /// <summary>The run loop threw.</summary>
    Error,

    /// <summary>The engine was disposed while running.</summary>
    Disposed,
}

/// <summary>Raised when the engine enters a new state.</summary>
public sealed class EngineStateChangedEventArgs : EventArgs
{
    public EngineStateChangedEventArgs(EngineState previousState, EngineState state)
    {
        PreviousState = previousState;
        State = state;
    }

    public EngineState PreviousState { get; }

    public EngineState State { get; }
}

/// <summary>Raised once, when a run begins.</summary>
public sealed class EngineStartedEventArgs : EventArgs
{
    public EngineStartedEventArgs(ClickerProfile profile, long? plannedActions)
    {
        Profile = profile;
        PlannedActions = plannedActions;
    }

    /// <summary>The snapshot the run is executing; editing the live profile will not affect it.</summary>
    public ClickerProfile Profile { get; }

    /// <summary>How many actions the run will perform, or null when unbounded.</summary>
    public long? PlannedActions { get; }
}

/// <summary>Raised once, when a run ends, whatever the cause.</summary>
public sealed class EngineStoppedEventArgs : EventArgs
{
    public EngineStoppedEventArgs(StopReason reason, long actionsPerformed, TimeSpan duration, Exception? error)
    {
        Reason = reason;
        ActionsPerformed = actionsPerformed;
        Duration = duration;
        Error = error;
    }

    public StopReason Reason { get; }

    /// <summary>Actions completed during this run. A double click counts as one.</summary>
    public long ActionsPerformed { get; }

    public TimeSpan Duration { get; }

    /// <summary>Set only when <see cref="Reason"/> is <see cref="StopReason.Error"/>.</summary>
    public Exception? Error { get; }
}

/// <summary>Raised after each completed action, so a UI can show a live counter.</summary>
public sealed class ActionExecutedEventArgs : EventArgs
{
    public ActionExecutedEventArgs(long actionIndex, ScreenPoint position, ActionKind kind)
    {
        ActionIndex = actionIndex;
        Position = position;
        Kind = kind;
    }

    /// <summary>One-based index of the action within the current run.</summary>
    public long ActionIndex { get; }

    /// <summary>Where the cursor was when the action completed.</summary>
    public ScreenPoint Position { get; }

    public ActionKind Kind { get; }
}
