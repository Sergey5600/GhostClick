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
/// Inputs held for the whole run (<see cref="InputChord.IsHeldWhileRunning"/>) are the
/// engine's own, not the plan's: it presses them when the first action may go, releases them
/// while paused or while the target window is not acceptable, and always releases them when
/// the run ends, however it ends.
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
    private readonly IWindowProbe? _windowProbe;
    private readonly object _sync = new();
    private readonly ManualResetEventSlim _pauseGate = new(initialState: true);

    /// <summary>
    /// Cancelled by <see cref="Pause"/>, so a run that holds inputs for the whole run wakes
    /// from a wait between actions and lets go of them at once. The run thread replaces it
    /// once it has seen the pause through.
    /// </summary>
    private CancellationTokenSource _pauseSignal = new();

    private ClickerProfile _profile = ClickerProfile.CreateDefault();
    private CancellationTokenSource? _cancellation;
    private Thread? _runThread;
    private StopReason _pendingStopReason = StopReason.User;
    private EngineState _state = EngineState.Idle;
    private long _currentRunActions;
    private bool _disposed;

    /// <param name="injector">Where input goes.</param>
    /// <param name="clock">Time source; the system clock by default.</param>
    /// <param name="screen">Desktop bounds used for clamping.</param>
    /// <param name="random">Randomness for jitter and interval spread.</param>
    /// <param name="windowProbe">
    /// When given, actions are held while they would land in this process's own windows, and
    /// <see cref="SafetySettings.ActiveWindowOnly"/> takes effect. Without one, neither check runs.
    /// </param>
    public AutoClickerEngine(
        IInputInjector injector,
        IClock? clock = null,
        IScreenGeometry? screen = null,
        IRandomSource? random = null,
        IWindowProbe? windowProbe = null)
    {
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _clock = clock ?? new SystemClock();
        _screen = screen ?? ResolveDefaultScreen();
        _random = random ?? new SystemRandomSource();
        _windowProbe = windowProbe;
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
    public long CurrentRunActions => Interlocked.Read(ref _currentRunActions);

    /// <summary>How long <see cref="Stop"/> waits for the run thread before giving up.</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How often a held action re-checks whether its target window is acceptable.</summary>
    public TimeSpan WindowPollInterval { get; set; } = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// After an action was held for its window, how long the same acceptable window must stay
    /// targeted before the action goes. Carrying the mouse from the clicker to the game passes
    /// over other windows on the way; without this the first of them would get the click.
    /// </summary>
    public TimeSpan WindowSettleTime { get; set; } = TimeSpan.FromMilliseconds(300);

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
        EngineStateChangedEventArgs? transition;

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
            ResetPauseSignalLocked();
            Interlocked.Exchange(ref _currentRunActions, 0);

            thread = new Thread(() => RunLoop(snapshot, cancellation.Token))
            {
                IsBackground = true,
                Name = "GhostClick.Engine",
                Priority = ThreadPriority.AboveNormal,
            };
            _runThread = thread;

            SetStateLocked(EngineState.Running, out transition);
        }

        RaiseStateChanged(transition);
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
        Thread? runThread;
        EngineStateChangedEventArgs? transition;

        lock (_sync)
        {
            if (_state == EngineState.Idle || _cancellation is null)
            {
                return null;
            }

            _pendingStopReason = reason;
            SetStateLocked(EngineState.Stopping, out transition);

            // Release the pause gate so a paused run can observe the cancellation.
            _pauseGate.Set();
            _cancellation.Cancel();
            runThread = _runThread;
        }

        RaiseStateChanged(transition);
        return runThread;
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

    /// <summary>
    /// Suspends a running session once the current action is done, so a pause never leaves a
    /// click or a timed hold pressed. Inputs held for the whole run are released as soon as
    /// the run is between actions, and pressed again on <see cref="Resume"/>.
    /// </summary>
    public bool Pause()
    {
        EngineStateChangedEventArgs? transition;

        lock (_sync)
        {
            if (_state != EngineState.Running)
            {
                return false;
            }

            _pauseGate.Reset();
            _pauseSignal.Cancel();
            SetStateLocked(EngineState.Paused, out transition);
        }

        RaiseStateChanged(transition);
        return true;
    }

    /// <summary>Resumes a paused session.</summary>
    public bool Resume()
    {
        EngineStateChangedEventArgs? transition;

        lock (_sync)
        {
            if (_state != EngineState.Paused)
            {
                return false;
            }

            _pauseGate.Set();
            SetStateLocked(EngineState.Running, out transition);
        }

        RaiseStateChanged(transition);
        return true;
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

        lock (_sync)
        {
            _pauseSignal.Dispose();
        }
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
        var context = new RunContext(startCursor, profile.Action.HeldWhileRunning);

        // Ask Windows for a 1 ms timer tick so short waits actually land near where we
        // asked. Released as soon as the run ends.
        using var timerScope = new HighResolutionTimerScope();

        try
        {
            var planner = new ActionPlanner(profile, _injector, _screen, _random);

            foreach (var step in planner.CreatePlan())
            {
                if (!AwaitTurn(step, profile, runStart, context, token, out var breach))
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
            Interlocked.Exchange(ref _currentRunActions, context.Actions);

            // What the interrupted action pressed comes up first, then what the run held around it.
            TryReleaseHeldInput(profile, context);
            ReleaseHeldWhileRunning(context, bestEffort: true);
            TryRestoreCursor(profile, context, startCursor);

            EngineStateChangedEventArgs? transition;

            lock (_sync)
            {
                _runThread = null;
                _cancellation?.Dispose();
                _cancellation = null;
                _pauseGate.Set();
                SetStateLocked(EngineState.Idle, out transition);
            }

            RaiseStateChanged(transition);
            Stopped?.Invoke(this, new EngineStoppedEventArgs(reason, context.Actions, duration, error));
        }
    }

    /// <summary>
    /// Blocks until <paramref name="step"/> may run: honours pause and cancellation, checks the
    /// safety limits, and holds the first press of an action while its target window is not
    /// acceptable. Returns false, with the reason, when a limit ends the run instead.
    /// </summary>
    private bool AwaitTurn(
        EngineStep step,
        ClickerProfile profile,
        TimeSpan runStart,
        RunContext context,
        CancellationToken token,
        out StopReason breach)
    {
        bool startsAction = !context.InAction &&
            step.Kind is EngineStepKind.MouseDown or EngineStepKind.KeyDown or EngineStepKind.Tick;
        bool held = false;
        long target = 0;
        long settling = 0;
        TimeSpan settlingSince = TimeSpan.Zero;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            // An action that has started is finished first: a pause between its press and its
            // release would leave the button down for as long as the pause lasts.
            if (!context.InAction && !_pauseGate.IsSet)
            {
                SitOutPause(profile, context, token);
            }

            if (TryGetLimitBreach(profile, runStart, context, out breach))
            {
                return false;
            }

            if (!startsAction)
            {
                return true;
            }

            if (TryGetTargetWindow(profile, context, out target))
            {
                if (!held)
                {
                    break;
                }

                // Coming out of a hold, the target has to stay put for a moment first.
                if (target != settling)
                {
                    settling = target;
                    settlingSince = _clock.Elapsed;
                }
                else if (_clock.Elapsed - settlingSince >= WindowSettleTime)
                {
                    break;
                }
            }
            else
            {
                held = true;
                settling = 0;

                // Held inputs must not keep going into a window the actions may not reach.
                ReleaseHeldWhileRunning(context);

                // Whatever the user does with the mouse while we wait is not a deviation from
                // our plan; the reference point is taken again once the action can go ahead.
                if (profile.Target.Mode == TargetMode.CurrentCursor)
                {
                    context.ExpectedCursor = null;
                }
            }

            _clock.Wait(WindowPollInterval, token);
        }

        context.InAction = true;

        if (profile.Safety.ActiveWindowOnly && context.LockedWindow == 0)
        {
            context.LockedWindow = target;
        }

        // The inputs held for the run go down with the first action, and again with the first
        // action after anything made the run let go of them.
        PressHeldWhileRunning(context);

        // Clicking at the cursor: the user aims, then lets go of the mouse. The first action
        // marks where it was left, so moving it later counts as taking it back.
        if (profile.Target.Mode == TargetMode.CurrentCursor && context.ExpectedCursor is null)
        {
            context.ExpectedCursor = _injector.GetCursorPosition();
        }

        return true;
    }

    /// <summary>
    /// False while the action about to start would land in the wrong window: one of this
    /// process's own windows, or - with <see cref="SafetySettings.ActiveWindowOnly"/> - any
    /// window other than the one the run locked onto. Clicks are judged by the window under
    /// the cursor, key presses by the foreground window. <paramref name="target"/> is the
    /// window the action would go to (0 when unknown), which is what a run locks onto.
    /// </summary>
    private bool TryGetTargetWindow(ClickerProfile profile, RunContext context, out long target)
    {
        target = 0;
        if (_windowProbe is null)
        {
            return true;
        }

        var action = profile.Action;
        long clicked = action.UsesMouse ? _windowProbe.GetWindowAt(_injector.GetCursorPosition()) : 0;
        long typed = action.UsesKeyboard ? _windowProbe.GetForegroundWindow() : 0;
        target = clicked != 0 ? clicked : typed;

        if (IsOwn(clicked) || IsOwn(typed))
        {
            return false;
        }

        if (!profile.Safety.ActiveWindowOnly)
        {
            return true;
        }

        // Before the lock exists, clicks and keys must at least agree on one window.
        long expected = context.LockedWindow != 0 ? context.LockedWindow : target;
        return Matches(clicked) && Matches(typed);

        bool IsOwn(long window) => window != 0 && _windowProbe.IsOwnWindow(window);

        bool Matches(long window) => window == 0 || expected == 0 || window == expected;
    }

    private void Execute(EngineStep step, ClickerProfile profile, RunContext context, CancellationToken token)
    {
        switch (step.Kind)
        {
            case EngineStepKind.Wait:
                if (context.InAction || context.Held.Length == 0)
                {
                    _clock.Wait(step.Delay, token);
                }
                else
                {
                    WaitBetweenActions(step.Delay, profile, context, token);
                }

                break;

            case EngineStepKind.MoveTo:
                _injector.MoveTo(step.Position);
                context.LastCommandedPosition = step.Position;
                context.ExpectedCursor = step.Position;
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

            case EngineStepKind.Tick:
                // Nothing to press: every input is held for the run. It still counts.
                break;

            default:
                throw new NotSupportedException($"Unknown step kind '{step.Kind}'.");
        }

        if (!step.CompletesAction)
        {
            return;
        }

        context.InAction = false;
        context.Actions++;
        Interlocked.Exchange(ref _currentRunActions, context.Actions);
        Statistics.RecordAction();

        ActionExecuted?.Invoke(this, new ActionExecutedEventArgs(
            context.Actions,
            context.HasCommandedMove ? context.LastCommandedPosition : _injector.GetCursorPosition(),
            profile.Action.UsesMouse ? ActionKind.Mouse : ActionKind.Keyboard));
    }

    /// <summary>
    /// A wait between actions in a run that holds inputs: a pause in the middle of it releases
    /// them at once rather than when the wait is over, and resuming presses them again where
    /// that is allowed. The wait itself then carries on for whatever is left of it.
    /// </summary>
    private void WaitBetweenActions(TimeSpan delay, ClickerProfile profile, RunContext context, CancellationToken token)
    {
        var end = _clock.Elapsed + delay;

        while (true)
        {
            var remaining = end - _clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                token.ThrowIfCancellationRequested();
                return;
            }

            using (var wake = CancellationTokenSource.CreateLinkedTokenSource(token, PauseToken()))
            {
                try
                {
                    _clock.Wait(remaining, wake.Token);
                    return;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // Paused; handled below, once the linked source is gone.
                }
            }

            SitOutPause(profile, context, token);
        }
    }

    /// <summary>
    /// Releases the inputs held for the run, blocks until the run is resumed (or stopped),
    /// then presses them again if the target window allows it. If it does not, the next
    /// action waits for the window and presses them then.
    /// </summary>
    private void SitOutPause(ClickerProfile profile, RunContext context, CancellationToken token)
    {
        ReleaseHeldWhileRunning(context);
        _pauseGate.Wait(token);

        lock (_sync)
        {
            ResetPauseSignalLocked();
        }

        if (context.Held.Length > 0 && context.HeldEverPressed && TryGetTargetWindow(profile, context, out _))
        {
            PressHeldWhileRunning(context);
        }
    }

    private CancellationToken PauseToken()
    {
        lock (_sync)
        {
            return _pauseSignal.Token;
        }
    }

    /// <summary>
    /// Arms a fresh pause signal once the last pause has been seen through. While the run is
    /// still paused the cancelled one stays, so the next wait does not miss that pause.
    /// Must be called under <see cref="_sync"/>.
    /// </summary>
    private void ResetPauseSignalLocked()
    {
        if (_pauseSignal.IsCancellationRequested && _state != EngineState.Paused)
        {
            _pauseSignal.Dispose();
            _pauseSignal = new CancellationTokenSource();
        }
    }

    /// <summary>Presses the inputs held for the run, modifiers first, unless they are already down.</summary>
    private void PressHeldWhileRunning(RunContext context)
    {
        if (context.HeldDown || context.Held.Length == 0)
        {
            return;
        }

        // Marked first, so a press that fails half way still has the rest released.
        context.HeldDown = true;
        context.HeldEverPressed = true;

        foreach (var modifier in context.HeldModifiers)
        {
            context.HeldKeys.Add(modifier);
            _injector.KeyDown(modifier);
        }

        foreach (var input in context.Held)
        {
            if (input.Key.TryGetMouseButton(out var button))
            {
                context.HeldButtons.Add(button);
                _injector.MouseButtonDown(button);
            }
            else
            {
                context.HeldKeys.Add(input.Key);
                _injector.KeyDown(input.Key);
            }
        }
    }

    /// <summary>
    /// Releases the inputs held for the run in reverse order, then their modifiers. When the
    /// run is ending (<paramref name="bestEffort"/>) a failing release does not stop the rest.
    /// </summary>
    private void ReleaseHeldWhileRunning(RunContext context, bool bestEffort = false)
    {
        if (!context.HeldDown)
        {
            return;
        }

        context.HeldDown = false;

        for (int i = context.Held.Length - 1; i >= 0; i--)
        {
            var key = context.Held[i].Key;
            if (key.TryGetMouseButton(out var button))
            {
                if (context.HeldButtons.Remove(button))
                {
                    Release(() => _injector.MouseButtonUp(button));
                }
            }
            else if (context.HeldKeys.Remove(key))
            {
                Release(() => _injector.KeyUp(key));
            }
        }

        for (int i = context.HeldModifiers.Length - 1; i >= 0; i--)
        {
            var modifier = context.HeldModifiers[i];
            if (context.HeldKeys.Remove(modifier))
            {
                Release(() => _injector.KeyUp(modifier));
            }
        }

        void Release(Action release)
        {
            if (!bestEffort)
            {
                release();
                return;
            }

            try
            {
                release();
            }
            catch (Exception)
            {
                // Best effort: the run is already ending.
            }
        }
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

        // Only meaningful once there is a reference: the last position the engine moved the
        // cursor to, or, when clicking at the cursor, where the first action found it.
        if (safety.StopOnUserCursorMove && context.ExpectedCursor is { } expected)
        {
            var actual = _injector.GetCursorPosition();
            if (actual.DistanceTo(expected) > safety.UserCursorMoveThresholdPixels)
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
    /// stuck down for the whole system. Release anything an action still holds; the inputs
    /// held for the whole run are left to <see cref="ReleaseHeldWhileRunning"/>.
    /// </summary>
    private void TryReleaseHeldInput(ClickerProfile profile, RunContext context)
    {
        var keepButtons = new HashSet<MouseButton>();
        var keepKeys = new HashSet<VirtualKey>();
        if (context.HeldDown)
        {
            foreach (var input in context.Held)
            {
                if (input.Key.TryGetMouseButton(out var held))
                {
                    keepButtons.Add(held);
                }
                else
                {
                    keepKeys.Add(input.Key);
                }
            }

            keepKeys.UnionWith(context.HeldModifiers);
        }

        foreach (var button in context.HeldButtons.Where(b => !keepButtons.Contains(b)).ToArray())
        {
            try
            {
                _injector.MouseButtonUp(button);
            }
            catch (Exception)
            {
                // Best effort: the run is already ending.
            }

            context.HeldButtons.Remove(button);
        }

        foreach (var key in context.HeldKeys.Where(k => !keepKeys.Contains(k)).ToArray())
        {
            try
            {
                _injector.KeyUp(key);
            }
            catch (Exception)
            {
                // Best effort: the run is already ending.
            }

            context.HeldKeys.Remove(key);
        }

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
        public RunContext(ScreenPoint startCursor, IReadOnlyList<InputChord> held)
        {
            LastCommandedPosition = startCursor;

            // One physical key is held once, the way its first entry says.
            Held = held.DistinctBy(i => i.Key).ToArray();
            HeldModifiers = ActionPlanner
                .EnumerateModifiers(Held.Aggregate(KeyModifiers.None, (all, i) => all | i.Modifiers))
                .ToArray();
        }

        /// <summary>The inputs held down for the whole run.</summary>
        public InputChord[] Held { get; }

        /// <summary>The modifiers held around them.</summary>
        public VirtualKey[] HeldModifiers { get; }

        /// <summary>True while <see cref="Held"/> is pressed.</summary>
        public bool HeldDown { get; set; }

        /// <summary>True once <see cref="Held"/> has been pressed at least once this run.</summary>
        public bool HeldEverPressed { get; set; }

        public long Actions { get; set; }

        public ScreenPoint LastCommandedPosition { get; set; }

        public bool HasCommandedMove { get; set; }

        /// <summary>Where the cursor should be, for the user-moved-the-mouse check; null when unknown.</summary>
        public ScreenPoint? ExpectedCursor { get; set; }

        /// <summary>True between the first press of an action and the release that completes it.</summary>
        public bool InAction { get; set; }

        /// <summary>The window an ActiveWindowOnly run locked onto; 0 until the first action.</summary>
        public long LockedWindow { get; set; }

        public HashSet<MouseButton> HeldButtons { get; } = new();

        public HashSet<VirtualKey> HeldKeys { get; } = new();
    }
}
