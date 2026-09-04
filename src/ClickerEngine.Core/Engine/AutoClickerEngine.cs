using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Planning;
using ClickerEngine.Core.Timing;

namespace ClickerEngine.Core.Engine;

/// <summary>
/// Runs a <see cref="ClickerProfile"/>: owns the run thread, the state machine, the failsafe
/// checks and the counters.
/// <para>
/// It knows nothing about UI. Everything it does goes through <see cref="IInputInjector"/>,
/// <see cref="IClock"/> and <see cref="IRandomSource"/>, and everything it reports comes out
/// as plain events - so it can be driven from WPF today and from an HTTP or IPC front end
/// later without touching this class.
/// </para>
/// <para>
/// Events are raised on the engine's own run thread. A UI must marshal them itself.
/// </para>
/// </summary>
public sealed class AutoClickerEngine : IDisposable
{
    private readonly IInputInjector _injector;
    private readonly IClock _clock;
    private readonly IScreenGeometry _screen;
    private readonly IRandomSource _random;
    private readonly object _sync = new();
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);

    private ClickerProfile _profile = ClickerProfile.CreateDefault();
    private CancellationTokenSource? _cancellation;
    private Thread? _runThread;
    private StopReason _pendingStopReason = StopReason.User;
    private EngineState _state = EngineState.Idle;
    private bool _disposed;

    public AutoClickerEngine(
        IInputInjector injector,
        IClock? clock = null,
        IScreenGeometry? screen = null,
        IRandomSource? random = null)
    {
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _clock = clock ?? new SystemClock();
        _screen = screen ?? ResolveDefaultScreen();
        _random = random ?? new SystemRandomSource();
    }

    /// <summary>Raised on every state transition.</summary>
    public event EventHandler<EngineStateChangedEventArgs>? StateChanged;

    /// <summary>Raised when a run begins.</summary>
    public event EventHandler<EngineStartedEventArgs>? Started;

    /// <summary>Raised when a run ends, whatever the cause.</summary>
    public event EventHandler<EngineStoppedEventArgs>? Stopped;

    /// <summary>Raised after each completed action.</summary>
    public event EventHandler<ActionExecutedEventArgs>? ActionExecuted;

    /// <summary>
    /// The profile to run. Assigning while a run is in progress does not affect that run:
    /// the engine executes a snapshot taken at <see cref="Start"/>.
    /// </summary>
    public ClickerProfile Profile
    {
        get
        {
            lock (_sync)
            {
                return _profile;
            }
        }

        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_sync)
            {
                _profile = value;
            }
        }
    }

    /// <summary>Current lifecycle state.</summary>
    public EngineState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>True while a run is active, paused included.</summary>
    public bool IsActive => State is EngineState.Running or EngineState.Paused;

    /// <summary>Counters across the whole session.</summary>
    public SessionStatistics Statistics { get; } = new();

    /// <summary>Actions completed in the current (or most recent) run.</summary>
    public long CurrentRunActions { get; private set; }

    /// <summary>How long <see cref="Stop"/> waits for the run thread before giving up.</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The injector backend in use, for display.</summary>
    public IInputInjector Injector => _injector;

    /// <summary>
    /// Starts a run. Does nothing if one is already active. Throws
    /// <see cref="ProfileValidationException"/> when the profile is unusable.
    /// </summary>
    public bool Start()
    {
        ClickerProfile snapshot;
        Thread thread;
        CancellationTokenSource cancellation;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state != EngineState.Idle)
            {
                return false;
            }

            snapshot = _profile.Clone();
            ProfileValidator.EnsureValid(snapshot);

            cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _pendingStopReason = StopReason.User;
            _pauseGate.Set();
            CurrentRunActions = 0;

            thread = new Thread(() => RunLoop(snapshot, cancellation.Token))
            {
                IsBackground = true,
                Name = "GhostClick.Engine",
                Priority = ThreadPriority.AboveNormal,
            };
            _runThread = thread;

            SetStateLocked(EngineState.Running, out var transition);
            RaiseStateChanged(transition);
        }

        Statistics.RecordRunStarted();
        Started?.Invoke(this, new EngineStartedEventArgs(
            snapshot,
            new ActionPlanner(snapshot, _injector, _screen, _random).PlannedActionCount));

        thread.Start();
        return true;
    }

    /// <summary>
    /// Requests a stop and waits (up to <see cref="StopTimeout"/>) for the run thread to
    /// unwind, so a subsequent <see cref="Start"/> is guaranteed to take effect.
    /// </summary>
    public bool Stop(StopReason reason = StopReason.User)
    {
        var thread = RequestStop(reason);
        if (thread is null)
        {
            return false;
        }

        if (thread != Thread.CurrentThread)
        {
            thread.Join(StopTimeout);
        }

        return true;
    }

    /// <summary>
    /// Requests a stop without waiting. Returns the run thread, or null if nothing was
    /// running. Safe to call from a hook callback.
    /// </summary>
    public Thread? RequestStop(StopReason reason = StopReason.User)
    {
        lock (_sync)
        {
            if (_state == EngineState.Idle || _cancellation is null)
            {
                return null;
            }

            _pendingStopReason = reason;

            if (_state != EngineState.Stopping)
            {
                SetStateLocked(EngineState.Stopping, out var transition);
                RaiseStateChanged(transition);
            }

            // Release the pause gate so a paused run can observe the cancellation.
            _pauseGate.Set();
            _cancellation.Cancel();
            return _runThread;
        }
    }

    /// <summary>Starts if idle, stops if active. This is what the start/stop hotkey calls.</summary>
    public void Toggle()
    {
        if (IsActive)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    /// <summary>Suspends a running session at the next step boundary.</summary>
    public bool Pause()
    {
        lock (_sync)
        {
            if (_state != EngineState.Running)
            {
                return false;
            }

            _pauseGate.Reset();
            SetStateLocked(EngineState.Paused, out var transition);
            RaiseStateChanged(transition);
            return true;
        }
    }

    /// <summary>Resumes a paused session.</summary>
    public bool Resume()
    {
        lock (_sync)
        {
            if (_state != EngineState.Paused)
            {
                return false;
            }

            _pauseGate.Set();
            SetStateLocked(EngineState.Running, out var transition);
            RaiseStateChanged(transition);
            return true;
        }
    }

    /// <summary>Pauses if running, resumes if paused.</summary>
    public void TogglePause()
    {
        if (!Pause())
        {
            Resume();
        }
    }

    /// <summary>Stops immediately with <see cref="StopReason.Failsafe"/>.</summary>
    public void EmergencyStop() => Stop(StopReason.Failsafe);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop(StopReason.Disposed);
        _pauseGate.Dispose();
        _cancellation?.Dispose();
    }

    private static IScreenGeometry ResolveDefaultScreen() => OperatingSystem.IsWindows()
        ? new Win32ScreenGeometry()
        : new FixedScreenGeometry(1920, 1080);

    private void RunLoop(ClickerProfile profile, CancellationToken token)
    {
        var reason = StopReason.Completed;
        Exception? error = null;

        var runStart = _clock.Elapsed;
        var startCursor = _injector.GetCursorPosition();
        var context = new RunContext(startCursor);

        // Ask Windows for a 1 ms timer tick so short waits actually land near where we
        // asked. Released as soon as the run ends.
        using var timerScope = new HighResolutionTimerScope();

        try
        {
            var planner = new ActionPlanner(profile, _injector, _screen, _random);

            foreach (var step in planner.CreatePlan())
            {
                token.ThrowIfCancellationRequested();
                _pauseGate.Wait(token);

                if (TryGetLimitBreach(profile, runStart, context, out var breach))
                {
                    reason = breach;
                    break;
                }

                Execute(step, profile, context, token);
            }
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                reason = _pendingStopReason;
            }
        }
        catch (Exception ex)
        {
            error = ex;
            reason = StopReason.Error;
        }
        finally
        {
            var duration = _clock.Elapsed - runStart;
            Statistics.RecordRunTime(duration);
            CurrentRunActions = context.Actions;

            TryReleaseHeldInput(profile, context);
            TryRestoreCursor(profile, context, startCursor);

            lock (_sync)
            {
                _runThread = null;
                _cancellation?.Dispose();
                _cancellation = null;
                _pauseGate.Set();
                SetStateLocked(EngineState.Idle, out var transition);
                RaiseStateChanged(transition);
            }

            Stopped?.Invoke(this, new EngineStoppedEventArgs(reason, context.Actions, duration, error));
        }
    }

    private void Execute(EngineStep step, ClickerProfile profile, RunContext context, CancellationToken token)
    {
        switch (step.Kind)
        {
            case EngineStepKind.Wait:
                _clock.Wait(step.Delay, token);
                break;

            case EngineStepKind.MoveTo:
                _injector.MoveTo(step.Position);
                context.LastCommandedPosition = step.Position;
                context.HasCommandedMove = true;
                break;

            case EngineStepKind.MouseDown:
                _injector.MouseButtonDown(step.Button);
                context.HeldButtons.Add(step.Button);
                break;

            case EngineStepKind.MouseUp:
                _injector.MouseButtonUp(step.Button);
                context.HeldButtons.Remove(step.Button);
                Statistics.RecordMouseClick();
                break;

            case EngineStepKind.KeyDown:
                _injector.KeyDown(step.Key);
                context.HeldKeys.Add(step.Key);
                break;

            case EngineStepKind.KeyUp:
                _injector.KeyUp(step.Key);
                context.HeldKeys.Remove(step.Key);
                if (!step.IsModifier)
                {
                    Statistics.RecordKeyPress();
                }

                break;

            default:
                throw new NotSupportedException($"Unknown step kind '{step.Kind}'.");
        }

        if (!step.CompletesAction)
        {
            return;
        }

        context.Actions++;
        CurrentRunActions = context.Actions;
        Statistics.RecordAction();

        ActionExecuted?.Invoke(this, new ActionExecutedEventArgs(
            context.Actions,
            context.HasCommandedMove ? context.LastCommandedPosition : _injector.GetCursorPosition(),
            profile.Action.Kind));
    }

    /// <summary>Checks the run against the safety limits before executing the next step.</summary>
    private bool TryGetLimitBreach(
        ClickerProfile profile,
        TimeSpan runStart,
        RunContext context,
        out StopReason reason)
    {
        var safety = profile.Safety;

        if (safety.MaxRunDurationSeconds > 0 &&
            (_clock.Elapsed - runStart).TotalSeconds >= safety.MaxRunDurationSeconds)
        {
            reason = StopReason.TimeLimit;
            return true;
        }

        if (safety.MaxActionsPerRun > 0 && context.Actions >= safety.MaxActionsPerRun)
        {
            reason = StopReason.ActionLimit;
            return true;
        }

        // Only meaningful once we have taken control of the cursor: in CurrentCursor mode the
        // user is supposed to be moving the mouse.
        if (safety.StopOnUserCursorMove &&
            context.HasCommandedMove &&
            profile.Target.Mode != TargetMode.CurrentCursor)
        {
            var actual = _injector.GetCursorPosition();
            if (actual.DistanceTo(context.LastCommandedPosition) > safety.UserCursorMoveThresholdPixels)
            {
                reason = StopReason.UserCursorMoved;
                return true;
            }
        }

        reason = StopReason.Completed;
        return false;
    }

    /// <summary>
    /// A run cancelled between a press and its release would otherwise leave a button or key
    /// stuck down for the whole system. Release anything still held.
    /// </summary>
    private void TryReleaseHeldInput(ClickerProfile profile, RunContext context)
    {
        foreach (var button in context.HeldButtons.ToArray())
        {
            try
            {
                _injector.MouseButtonUp(button);
            }
            catch (Exception)
            {
                // Best effort: the run is already ending.
            }
        }

        foreach (var key in context.HeldKeys.ToArray())
        {
            try
            {
                _injector.KeyUp(key);
            }
            catch (Exception)
            {
                // Best effort: the run is already ending.
            }
        }

        context.HeldButtons.Clear();
        context.HeldKeys.Clear();
        _ = profile;
    }

    private void TryRestoreCursor(ClickerProfile profile, RunContext context, ScreenPoint startCursor)
    {
        if (!profile.Target.RestoreCursorOnStop || !context.HasCommandedMove)
        {
            return;
        }

        try
        {
            _injector.MoveTo(startCursor);
        }
        catch (Exception)
        {
            // Best effort: the run is already ending.
        }
    }

    /// <summary>Must be called under <see cref="_sync"/>.</summary>
    private void SetStateLocked(EngineState state, out EngineStateChangedEventArgs? transition)
    {
        if (_state == state)
        {
            transition = null;
            return;
        }

        transition = new EngineStateChangedEventArgs(_state, state);
        _state = state;
    }

    private void RaiseStateChanged(EngineStateChangedEventArgs? transition)
    {
        if (transition is not null)
        {
            StateChanged?.Invoke(this, transition);
        }
    }

    /// <summary>Mutable per-run bookkeeping, kept out of the engine's shared state.</summary>
    private sealed class RunContext
    {
        public RunContext(ScreenPoint startCursor) => LastCommandedPosition = startCursor;

        public long Actions { get; set; }

        public ScreenPoint LastCommandedPosition { get; set; }

        public bool HasCommandedMove { get; set; }

        public HashSet<MouseButton> HeldButtons { get; } = new();

        public HashSet<VirtualKey> HeldKeys { get; } = new();
    }
}
