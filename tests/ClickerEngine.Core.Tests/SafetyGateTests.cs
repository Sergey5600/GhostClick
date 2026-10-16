using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Profiles;
using ClickerEngine.Core.Timing;
using Xunit;

namespace ClickerEngine.Core.Tests;

/// <summary>
/// The checks that keep input out of the wrong place: the window gate (own window,
/// ActiveWindowOnly) and the user-moved-the-mouse stop.
/// </summary>
public class SafetyGateTests
{
    private const long OwnWindow = 1;
    private const long GameWindow = 2;
    private const long BrowserWindow = 3;

    private static readonly FixedScreenGeometry Screen = new(1920, 1080);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void ClicksAreHeldWhileTheCursorIsOverTheClickersOwnWindow()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(10, 10));
        var probe = new FakeWindowProbe { UnderCursor = OwnWindow };
        using var engine = CreateEngine(injector, probe, out var stopped);
        engine.Profile = FixedCount(3);

        engine.Start();
        Thread.Sleep(150);

        Assert.Equal(EngineState.Running, engine.State);
        Assert.Empty(injector.Events);

        probe.UnderCursor = GameWindow;

        var args = WaitForStop(stopped);
        Assert.Equal(StopReason.Completed, args.Reason);
        Assert.Equal(3, args.ActionsPerformed);
    }

    [Fact]
    public void KeyPressesAreJudgedByTheForegroundWindowNotTheCursor()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = OwnWindow, Foreground = GameWindow };
        using var engine = CreateEngine(injector, probe, out var stopped);

        var profile = FixedCount(3);
        profile.Action.Inputs = [new InputChord(VirtualKey.E)];
        engine.Profile = profile;

        engine.Start();

        Assert.Equal(StopReason.Completed, WaitForStop(stopped).Reason);
        Assert.Equal(3, injector.Events.Count(e => e.Kind == InjectedEventKind.KeyDown));
    }

    [Fact]
    public void AnUnknownWindowNeverHoldsTheRun()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = 0, Foreground = 0 };
        using var engine = CreateEngine(injector, probe, out var stopped);

        var profile = FixedCount(3);
        profile.Safety.ActiveWindowOnly = true;
        engine.Profile = profile;

        engine.Start();

        Assert.Equal(3, WaitForStop(stopped).ActionsPerformed);
    }

    [Fact]
    public void ActiveWindowOnly_LocksOntoTheFirstWindowAndHoldsWhileAnotherIsTargeted()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = GameWindow };
        using var engine = CreateEngine(injector, probe, out var stopped);

        var profile = FixedCount(1000);
        profile.Interval.DelayMs = 2;
        profile.Safety.ActiveWindowOnly = true;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentRunActions >= 3));

        probe.UnderCursor = BrowserWindow;
        Thread.Sleep(60);
        long heldAt = engine.CurrentRunActions;
        Thread.Sleep(150);

        // At most the action that was already under way when the window changed.
        Assert.Equal(heldAt, engine.CurrentRunActions);
        Assert.Equal(EngineState.Running, engine.State);

        probe.UnderCursor = GameWindow;
        Assert.True(SpinUntil(() => engine.CurrentRunActions > heldAt + 3));

        engine.Stop();
        Assert.Equal(StopReason.User, WaitForStop(stopped).Reason);
    }

    [Fact]
    public void WithoutActiveWindowOnly_SwitchingWindowsDoesNotHold()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = GameWindow };
        using var engine = CreateEngine(injector, probe, out var stopped);

        var profile = FixedCount(1000);
        profile.Interval.DelayMs = 2;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentRunActions >= 3));

        probe.UnderCursor = BrowserWindow;
        long before = engine.CurrentRunActions;
        Assert.True(SpinUntil(() => engine.CurrentRunActions > before + 3));

        engine.Stop();
        WaitForStop(stopped);
    }

    [Fact]
    public void Pause_StillWorksWhileAnActionIsHeld()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = OwnWindow };
        using var engine = CreateEngine(injector, probe, out var stopped);
        engine.Profile = FixedCount(1);

        engine.Start();
        Assert.True(engine.Pause());

        probe.UnderCursor = GameWindow;
        Thread.Sleep(100);
        Assert.Empty(injector.Events);

        Assert.True(engine.Resume());
        Assert.Equal(StopReason.Completed, WaitForStop(stopped).Reason);
    }

    [Fact]
    public void StopOnUserCursorMove_AtTheCursor_StopsWhenTheMouseIsTakenBack()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(100, 100));
        using var engine = CreateEngine(injector, probe: null, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 2;
        profile.Safety.StopOnUserCursorMove = true;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentRunActions >= 2));

        injector.SetCursorPosition(new ScreenPoint(600, 600));

        Assert.Equal(StopReason.UserCursorMoved, WaitForStop(stopped).Reason);
    }

    [Fact]
    public void StopOnUserCursorMove_AtTheCursor_ToleratesSmallTremors()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(100, 100));
        using var engine = CreateEngine(injector, probe: null, out var stopped);

        var profile = FixedCount(20);
        profile.Interval.DelayMs = 2;
        profile.Safety.StopOnUserCursorMove = true;
        profile.Safety.UserCursorMoveThresholdPixels = 50;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentRunActions >= 2));
        injector.SetCursorPosition(new ScreenPoint(110, 95));

        Assert.Equal(StopReason.Completed, WaitForStop(stopped).Reason);
    }

    [Fact]
    public void StopOnUserCursorMove_AtTheCursor_MeasuresFromWhereTheFirstActionWent()
    {
        // Start from the clicker's own window, then carry the mouse over to the target: the
        // run waits for that, and the carrying is aiming, not taking the mouse back.
        var injector = new RecordingInputInjector(new ScreenPoint(100, 100));
        var probe = new FakeWindowProbe { WindowAtPoint = p => p.X < 300 ? OwnWindow : GameWindow };
        using var engine = CreateEngine(injector, probe, out var stopped);

        var profile = FixedCount(5);
        profile.Interval.DelayMs = 2;
        profile.Safety.StopOnUserCursorMove = true;
        engine.Profile = profile;

        engine.Start();
        Thread.Sleep(80);
        injector.SetCursorPosition(new ScreenPoint(900, 700));

        var args = WaitForStop(stopped);
        Assert.Equal(StopReason.Completed, args.Reason);
        Assert.Equal(5, args.ActionsPerformed);
    }

    [Fact]
    public void ActionExecuted_ReportsAMixedChordAsAMouseAction()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, probe: null, out var stopped);

        var profile = FixedCount(1);
        profile.Action.Inputs = [new InputChord(VirtualKey.E), InputChord.Mouse(MouseButton.Right)];
        engine.Profile = profile;

        ActionKind? kind = null;
        engine.ActionExecuted += (_, e) => kind = e.Kind;

        engine.Start();
        WaitForStop(stopped);

        Assert.Equal(ActionKind.Mouse, kind);
    }

    [Fact]
    public void DryRunAwareProbe_ReportsUnknownWindowsDuringADryRun()
    {
        var inner = new FakeWindowProbe { UnderCursor = OwnWindow, Foreground = OwnWindow };
        var injector = new SwitchableInputInjector(new RecordingInputInjector(), new RecordingInputInjector());
        var probe = new DryRunAwareWindowProbe(inner, injector);

        Assert.Equal(OwnWindow, probe.GetWindowAt(ScreenPoint.Zero));
        Assert.True(probe.IsOwnWindow(OwnWindow));

        injector.DryRun = true;

        Assert.Equal(0, probe.GetWindowAt(ScreenPoint.Zero));
        Assert.Equal(0, probe.GetForegroundWindow());
        Assert.False(probe.IsOwnWindow(OwnWindow));
    }

    [Fact]
    public void Recorder_DropsTheOldestEventsBeyondItsCapacity()
    {
        var recorder = new RecordingInputInjector { Capacity = 3 };

        foreach (var key in new[] { VirtualKey.A, VirtualKey.B, VirtualKey.C, VirtualKey.D, VirtualKey.E })
        {
            recorder.KeyDown(key);
        }

        Assert.Equal(new[] { VirtualKey.C, VirtualKey.D, VirtualKey.E }, recorder.Events.Select(e => e.Key));
    }

    [Fact]
    public void ProfileManager_BuildsNewProfilesWithTheFrontEndsFactory()
    {
        var manager = new ProfileManager(new InMemoryProfileStore())
        {
            NewProfileFactory = name =>
            {
                var profile = ClickerProfile.CreateDefault(name);
                profile.Safety.ActiveWindowOnly = true;
                return profile;
            },
        };

        var first = manager.LoadLastUsedOrDefault();
        var created = manager.CreateNew("Farm");

        Assert.Equal("Default", first.Name);
        Assert.True(first.Safety.ActiveWindowOnly);
        Assert.Equal("Farm", created.Name);
        Assert.True(created.Safety.ActiveWindowOnly);
    }

    private static ClickerProfile FixedCount(int count)
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = count;
        profile.Interval.DelayMs = 1;
        return profile;
    }

    private static AutoClickerEngine CreateEngine(
        IInputInjector injector,
        IWindowProbe? probe,
        out StopSignal stopped)
    {
        var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance, probe)
        {
            WindowPollInterval = TimeSpan.FromMilliseconds(2),
        };

        var signal = new StopSignal();
        engine.Stopped += (_, e) => signal.Set(e);
        stopped = signal;
        return engine;
    }

    private static EngineStoppedEventArgs WaitForStop(StopSignal stopped)
    {
        Assert.True(stopped.Wait(Timeout), "The engine did not stop within the timeout.");
        return stopped.Args!;
    }

    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, Timeout);

    private sealed class StopSignal
    {
        private readonly ManualResetEventSlim _signal = new(false);

        public EngineStoppedEventArgs? Args { get; private set; }

        public void Set(EngineStoppedEventArgs args)
        {
            Args = args;
            _signal.Set();
        }

        public bool Wait(TimeSpan timeout) => _signal.Wait(timeout);
    }

    /// <summary>A desktop whose windows the test rearranges by hand.</summary>
    private sealed class FakeWindowProbe : IWindowProbe
    {
        private long _underCursor = GameWindow;
        private long _foreground = GameWindow;

        public long UnderCursor
        {
            get => Interlocked.Read(ref _underCursor);
            set => Interlocked.Exchange(ref _underCursor, value);
        }

        public long Foreground
        {
            get => Interlocked.Read(ref _foreground);
            set => Interlocked.Exchange(ref _foreground, value);
        }

        public Func<ScreenPoint, long>? WindowAtPoint { get; init; }

        public long GetForegroundWindow() => Foreground;

        public long GetWindowAt(ScreenPoint point) => WindowAtPoint?.Invoke(point) ?? UnderCursor;

        public bool IsOwnWindow(long window) => window == OwnWindow;
    }
}
