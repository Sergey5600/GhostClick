using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Timing;
using Xunit;

namespace ClickerEngine.Core.Tests;

/// <summary>
/// Inputs held for the whole run: down while the run goes, up whenever it pauses, stops or
/// fails, and never left down.
/// </summary>
public class HoldTests
{
    private const long OwnWindow = 1;
    private const long GameWindow = 2;

    private static readonly FixedScreenGeometry Screen = new(1920, 1080);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void HeldInput_GoesDownWithTheRunAndUpWhenItStops()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 5;
        profile.Action.Inputs = [Held(VirtualKey.W), Held(VirtualKey.A), InputChord.Mouse(MouseButton.Left)];
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.Statistics.MouseClicks >= 3));
        engine.Stop();
        Assert.Equal(StopReason.User, WaitForStop(stopped).Reason);

        var events = injector.Events;

        // Pressed once, before the first click, and held through all of them.
        Assert.Equal(new[] { "KeyDown W", "KeyDown A", "MouseDown Left @ (0, 0)" }, events.Take(3).Select(e => e.ToString()));
        Assert.Equal(1, events.Count(e => e.Kind == InjectedEventKind.KeyDown && e.Key == VirtualKey.W));

        // Released last, in reverse order.
        Assert.Equal(new[] { "KeyUp A", "KeyUp W" }, events.TakeLast(2).Select(e => e.ToString()));
        AssertNothingLeftDown(events);
    }

    [Fact]
    public void Stop_MidClick_ReleasesTheClickBeforeWhatTheRunHolds()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Action.Inputs = [Held(VirtualKey.W), new InputChord(VirtualKey.LeftButton) { PressMs = 5_000 }];
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => Count(injector, InjectedEventKind.MouseDown) == 1));
        engine.Stop();
        WaitForStop(stopped);

        Assert.Equal(
            new[] { "KeyDown W", "MouseDown Left @ (0, 0)", "MouseUp Left @ (0, 0)", "KeyUp W" },
            injector.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void HeldInput_KeepsItsModifiersDownAndReleasesThemAfterIt()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 5;
        profile.Action.Inputs = [Held(VirtualKey.W, KeyModifiers.Shift), new InputChord(VirtualKey.E, KeyModifiers.Shift) { PressMs = 1 }];
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.Statistics.KeyPresses >= 3));
        engine.Stop();
        WaitForStop(stopped);

        var events = injector.Events;
        Assert.Equal(new[] { "KeyDown LeftShift", "KeyDown W", "KeyDown E" }, events.Take(3).Select(e => e.ToString()));
        Assert.Equal(1, events.Count(e => e.Kind == InjectedEventKind.KeyDown && e.Key == VirtualKey.LeftShift));
        Assert.Equal(new[] { "KeyUp W", "KeyUp LeftShift" }, events.TakeLast(2).Select(e => e.ToString()));
    }

    [Fact]
    public void OnlyHeldInputs_HoldForTheCountTimesTheIntervalThenLetGo()
    {
        var injector = new RecordingInputInjector();
        var clock = new VirtualClock();
        using var engine = new AutoClickerEngine(injector, clock, Screen, NeutralRandomSource.Instance);
        var stopped = Subscribe(engine);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 10;
        profile.Interval.DelayMs = 1000;
        profile.Action.Inputs = [Held(VirtualKey.W), Held(VirtualKey.A)];
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.Completed, args.Reason);
        Assert.Equal(10, args.ActionsPerformed);
        Assert.Equal(TimeSpan.FromSeconds(9), clock.Elapsed);
        Assert.Equal(
            new[] { "KeyDown W", "KeyDown A", "KeyUp A", "KeyUp W" },
            injector.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void Pause_ReleasesHeldInputsAtOnceAndResumePressesThemAgain()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        // A long interval: the pause has to cut into the wait, not wait for it to end.
        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 30_000;
        profile.Action.Inputs = [Held(VirtualKey.W)];
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => Count(injector, InjectedEventKind.KeyDown) == 1));

        Assert.True(engine.Pause());
        Assert.True(SpinUntil(() => Count(injector, InjectedEventKind.KeyUp) == 1, TimeSpan.FromSeconds(2)));

        Thread.Sleep(50);
        Assert.Equal(1, Count(injector, InjectedEventKind.KeyDown));

        Assert.True(engine.Resume());
        Assert.True(SpinUntil(() => Count(injector, InjectedEventKind.KeyDown) == 2, TimeSpan.FromSeconds(2)));

        engine.Stop();
        WaitForStop(stopped);

        Assert.Equal(
            new[] { "KeyDown W", "KeyUp W", "KeyDown W", "KeyUp W" },
            injector.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void Pause_AgainRightAfterResumeIsNotMissed()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 30_000;
        profile.Action.Inputs = [Held(VirtualKey.W)];
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => Count(injector, InjectedEventKind.KeyDown) == 1));

        for (int i = 0; i < 5; i++)
        {
            engine.Pause();
            engine.Resume();
        }

        engine.Pause();

        // However the pauses and resumes interleaved with the run thread, it ends paused with W up.
        Assert.True(SpinUntil(
            () => Count(injector, InjectedEventKind.KeyUp) == Count(injector, InjectedEventKind.KeyDown),
            TimeSpan.FromSeconds(2)));
        Thread.Sleep(50);
        Assert.Equal(Count(injector, InjectedEventKind.KeyUp), Count(injector, InjectedEventKind.KeyDown));

        engine.Stop();
        WaitForStop(stopped);
        AssertNothingLeftDown(injector.Events);
    }

    [Fact]
    public void EmergencyStop_WhilePaused_LeavesNothingDown()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 10;
        profile.Action.Inputs = [Held(VirtualKey.W), InputChord.Mouse(MouseButton.Left)];
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.Statistics.MouseClicks >= 1));
        engine.Pause();
        engine.EmergencyStop();

        Assert.Equal(StopReason.Failsafe, WaitForStop(stopped).Reason);
        AssertNothingLeftDown(injector.Events);
    }

    [Fact]
    public void AnErrorMidRun_StillReleasesTheHeldInputs()
    {
        var recorder = new RecordingInputInjector();
        var injector = new ClicksFailInjector(recorder);
        using var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance);
        var stopped = Subscribe(engine);

        var profile = ClickerProfile.CreateDefault();
        profile.Action.Inputs = [Held(VirtualKey.W, KeyModifiers.Control), InputChord.Mouse(MouseButton.Left)];
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.Error, args.Reason);
        Assert.Equal(
            new[] { "KeyDown LeftControl", "KeyDown W", "KeyUp W", "KeyUp LeftControl" },
            recorder.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void HeldInput_WaitsForTheTargetWindowAndLetsGoWhenItIsLost()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { Foreground = OwnWindow };
        using var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance, probe)
        {
            WindowPollInterval = TimeSpan.FromMilliseconds(2),
            WindowSettleTime = TimeSpan.FromMilliseconds(10),
        };
        var stopped = Subscribe(engine);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 5;
        profile.Action.Inputs = [Held(VirtualKey.W), new InputChord(VirtualKey.E) { PressMs = 1 }];
        engine.Profile = profile;

        // Started from the clicker's own window: nothing may go down there.
        engine.Start();
        Thread.Sleep(100);
        Assert.Empty(injector.Events);

        probe.Foreground = GameWindow;
        Assert.True(SpinUntil(() => engine.Statistics.KeyPresses >= 2));
        Assert.Equal("KeyDown W", injector.Events[0].ToString());

        // Back to the clicker: W comes up and stays up.
        probe.Foreground = OwnWindow;
        Assert.True(SpinUntil(() => injector.Events.Any(e => e.ToString() == "KeyUp W")));
        int whileAway = injector.Events.Count;
        Thread.Sleep(60);
        Assert.Equal(whileAway, injector.Events.Count);

        // And into the game again: W goes down once more.
        probe.Foreground = GameWindow;
        Assert.True(SpinUntil(() => injector.Events.Count(e => e.ToString() == "KeyDown W") == 2));

        engine.Stop();
        WaitForStop(stopped);
        AssertNothingLeftDown(injector.Events);
    }

    private static InputChord Held(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None) =>
        new(key, modifiers) { Mode = PressMode.Hold };

    private static int Count(RecordingInputInjector injector, InjectedEventKind kind) =>
        injector.Events.Count(e => e.Kind == kind);

    /// <summary>Every key and button that went down came up again.</summary>
    private static void AssertNothingLeftDown(IReadOnlyList<InjectedEvent> events)
    {
        var down = new HashSet<string>();
        foreach (var e in events)
        {
            string id = e.Kind is InjectedEventKind.MouseDown or InjectedEventKind.MouseUp ? "mouse " + e.Button : "key " + e.Key;
            switch (e.Kind)
            {
                case InjectedEventKind.KeyDown or InjectedEventKind.MouseDown:
                    down.Add(id);
                    break;
                case InjectedEventKind.KeyUp or InjectedEventKind.MouseUp:
                    down.Remove(id);
                    break;
            }
        }

        Assert.Empty(down);
    }

    private static AutoClickerEngine CreateEngine(IInputInjector injector, out StopSignal stopped)
    {
        var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance);
        stopped = Subscribe(engine);
        return engine;
    }

    private static StopSignal Subscribe(AutoClickerEngine engine)
    {
        var signal = new StopSignal();
        engine.Stopped += (_, e) => signal.Set(e);
        return signal;
    }

    private static EngineStoppedEventArgs WaitForStop(StopSignal stopped)
    {
        Assert.True(stopped.Wait(Timeout), "The engine did not stop within the timeout.");
        return stopped.Args!;
    }

    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, Timeout);

    private static bool SpinUntil(Func<bool> condition, TimeSpan timeout) => SpinWait.SpinUntil(condition, timeout);

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

    /// <summary>Keys go through; mouse buttons are refused, the way UIPI refuses an elevated window.</summary>
    private sealed class ClicksFailInjector : IInputInjector
    {
        private readonly RecordingInputInjector _inner;

        public ClicksFailInjector(RecordingInputInjector inner) => _inner = inner;

        public string Name => "Clicks fail";

        public ScreenPoint GetCursorPosition() => _inner.GetCursorPosition();

        public void MoveTo(ScreenPoint position) => _inner.MoveTo(position);

        public void MouseButtonDown(MouseButton button) => throw new InputInjectionException("blocked");

        public void MouseButtonUp(MouseButton button) => _inner.MouseButtonUp(button);

        public void KeyDown(VirtualKey key) => _inner.KeyDown(key);

        public void KeyUp(VirtualKey key) => _inner.KeyUp(key);
    }

    private sealed class FakeWindowProbe : IWindowProbe
    {
        private long _foreground = GameWindow;

        public long Foreground
        {
            get => Interlocked.Read(ref _foreground);
            set => Interlocked.Exchange(ref _foreground, value);
        }

        public long GetForegroundWindow() => Foreground;

        public long GetWindowAt(ScreenPoint point) => GameWindow;

        public bool IsOwnWindow(long window) => window == OwnWindow;
    }
}
