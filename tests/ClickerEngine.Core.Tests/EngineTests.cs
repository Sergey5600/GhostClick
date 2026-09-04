using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Timing;
using Xunit;

namespace ClickerEngine.Core.Tests;

public class EngineTests
{
    private static readonly FixedScreenGeometry Screen = new(1920, 1080);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void FiniteRun_InjectsTheExpectedInputAndReportsCompletion()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(200, 200));
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 5;
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        Assert.True(engine.Start());
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.Completed, args.Reason);
        Assert.Equal(5, args.ActionsPerformed);
        Assert.Equal(5, engine.Statistics.MouseClicks);
        Assert.Equal(5, engine.Statistics.Actions);
        Assert.Equal(EngineState.Idle, engine.State);

        Assert.Equal(
            5,
            injector.Events.Count(e => e.Kind == InjectedEventKind.MouseDown && e.Button == MouseButton.Left));
    }

    [Fact]
    public void Toggle_StartsThenStopsAnInfiniteRun()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 5;
        engine.Profile = profile;

        engine.Toggle();
        Assert.True(SpinUntil(() => engine.State == EngineState.Running));
        Assert.True(SpinUntil(() => engine.Statistics.MouseClicks > 0));

        engine.Toggle();

        var args = WaitForStop(stopped);
        Assert.Equal(StopReason.User, args.Reason);
        Assert.Equal(EngineState.Idle, engine.State);
        Assert.False(engine.IsActive);
    }

    [Fact]
    public void Start_IsIgnoredWhileAlreadyRunning()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out _);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 20;
        engine.Profile = profile;

        Assert.True(engine.Start());
        Assert.False(engine.Start());

        engine.Stop();
        Assert.Equal(1, engine.Statistics.Runs);
    }

    [Fact]
    public void Stop_ReleasesAButtonThatWasStillHeldDown()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 1;

        // A long hold means a stop is very likely to land between the press and the release.
        profile.Action.HoldDurationMs = 5_000;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => injector.Events.Any(e => e.Kind == InjectedEventKind.MouseDown)));

        engine.Stop();
        WaitForStop(stopped);

        // Whatever the interleaving, the button must not be left down: every press has a release.
        int downs = injector.Events.Count(e => e.Kind == InjectedEventKind.MouseDown);
        int ups = injector.Events.Count(e => e.Kind == InjectedEventKind.MouseUp);
        Assert.Equal(downs, ups);
    }

    [Fact]
    public void PauseAndResume_SuspendsWithoutEndingTheRun()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out _);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.Statistics.MouseClicks > 0));

        Assert.True(engine.Pause());
        Assert.Equal(EngineState.Paused, engine.State);
        Assert.True(engine.IsActive);

        long clicksWhilePaused = engine.Statistics.MouseClicks;
        Thread.Sleep(60);
        Assert.Equal(clicksWhilePaused, engine.Statistics.MouseClicks);

        Assert.True(engine.Resume());
        Assert.Equal(EngineState.Running, engine.State);
        Assert.True(SpinUntil(() => engine.Statistics.MouseClicks > clicksWhilePaused));

        engine.Stop();
    }

    [Fact]
    public void Stop_WhilePaused_UnblocksTheRunLoop()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.Statistics.MouseClicks > 0));
        engine.Pause();

        engine.Stop(StopReason.Failsafe);

        var args = WaitForStop(stopped);
        Assert.Equal(StopReason.Failsafe, args.Reason);
        Assert.Equal(EngineState.Idle, engine.State);
    }

    [Fact]
    public void ActionLimit_StopsTheRun()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 0;
        profile.Safety.MaxActionsPerRun = 12;
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.ActionLimit, args.Reason);
        Assert.Equal(12, args.ActionsPerformed);
    }

    [Fact]
    public void TimeLimit_StopsTheRun()
    {
        var injector = new RecordingInputInjector();
        var clock = new VirtualClock();
        using var engine = new AutoClickerEngine(injector, clock, Screen, NeutralRandomSource.Instance);
        var stopped = Subscribe(engine);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 1000;      // virtual time, so this costs nothing real
        profile.Safety.MaxRunDurationSeconds = 5;
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.TimeLimit, args.Reason);

        // Actions land at t = 0, 1, 2, 3 and 4 s. The next one would need the clock to pass
        // 5 s first, and the limit check in front of it is what ends the run.
        Assert.Equal(5, args.ActionsPerformed);
    }

    [Fact]
    public void RestoreCursorOnStop_PutsTheCursorBackWhereItWas()
    {
        var start = new ScreenPoint(777, 333);
        var injector = new RecordingInputInjector(start);
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 2;
        profile.Interval.DelayMs = 1;
        profile.Target.Mode = TargetMode.FixedPoint;
        profile.Target.FixedPoint = new ClickPoint(50, 50);
        profile.Target.RestoreCursorOnStop = true;
        engine.Profile = profile;

        engine.Start();
        WaitForStop(stopped);

        Assert.Equal(start, injector.GetCursorPosition());
        Assert.Equal(start, injector.Events[^1].Position);
    }

    [Fact]
    public void RestoreCursorOnStop_IsSkippedWhenTheEngineNeverMovedTheCursor()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(10, 10));
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 2;
        profile.Interval.DelayMs = 1;
        profile.Target.RestoreCursorOnStop = true;
        engine.Profile = profile;

        engine.Start();
        WaitForStop(stopped);

        Assert.DoesNotContain(injector.Events, e => e.Kind == InjectedEventKind.Move);
    }

    [Fact]
    public void InvalidProfile_FailsAtStartRatherThanClickingWrongly()
    {
        using var engine = CreateEngine(new RecordingInputInjector(), out _);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 0;
        engine.Profile = profile;

        var error = Assert.Throws<ProfileValidationException>(() => engine.Start());
        Assert.Contains(error.Errors, e => e.Path == "Repeat.Count");
        Assert.Equal(EngineState.Idle, engine.State);
    }

    [Fact]
    public void ProfileEditsDuringARunDoNotAffectThatRun()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 3;
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        engine.Start();

        // Swapping the button mid-run must not change what the running plan emits.
        profile.Action.Button = MouseButton.Right;

        var args = WaitForStop(stopped);
        Assert.Equal(3, args.ActionsPerformed);
        Assert.All(
            injector.Events.Where(e => e.Kind == InjectedEventKind.MouseDown),
            e => Assert.Equal(MouseButton.Left, e.Button));
    }

    [Fact]
    public void KeyboardRun_PressesTheConfiguredKey()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 4;
        profile.Interval.DelayMs = 1;
        profile.Action.Kind = ActionKind.Keyboard;
        profile.Action.Key = VirtualKey.E;
        engine.Profile = profile;

        engine.Start();
        WaitForStop(stopped);

        Assert.Equal(4, engine.Statistics.KeyPresses);
        Assert.Equal(0, engine.Statistics.MouseClicks);
        Assert.All(
            injector.Events.Where(e => e.Kind == InjectedEventKind.KeyDown),
            e => Assert.Equal(VirtualKey.E, e.Key));
    }

    [Fact]
    public void ModifiersDoNotInflateTheKeyPressCounter()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 3;
        profile.Interval.DelayMs = 1;
        profile.Action.Kind = ActionKind.Keyboard;
        profile.Action.Key = VirtualKey.C;
        profile.Action.KeyModifiers = KeyModifiers.Control;
        engine.Profile = profile;

        engine.Start();
        WaitForStop(stopped);

        Assert.Equal(3, engine.Statistics.KeyPresses);
        Assert.Equal(6, injector.Events.Count(e => e.Kind == InjectedEventKind.KeyDown));
    }

    [Fact]
    public void ErrorsFromTheInjectorEndTheRunAndAreReported()
    {
        var injector = new ThrowingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.Error, args.Reason);
        Assert.IsType<InputInjectionException>(args.Error);
        Assert.Equal(EngineState.Idle, engine.State);
    }

    [Fact]
    public void StateChanges_AreReportedInOrder()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var states = new List<EngineState>();
        engine.StateChanged += (_, e) =>
        {
            lock (states)
            {
                states.Add(e.State);
            }
        };

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 2;
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        engine.Start();
        WaitForStop(stopped);

        lock (states)
        {
            Assert.Equal(new[] { EngineState.Running, EngineState.Idle }, states);
        }
    }

    [Fact]
    public void ActionExecuted_FiresOncePerActionWithARisingIndex()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);

        var indices = new List<long>();
        engine.ActionExecuted += (_, e) =>
        {
            lock (indices)
            {
                indices.Add(e.ActionIndex);
            }
        };

        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 6;
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        engine.Start();
        WaitForStop(stopped);

        lock (indices)
        {
            Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, indices);
        }
    }

    private static AutoClickerEngine CreateEngine(IInputInjector injector, out BlockingStop stopped)
    {
        var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance);
        stopped = Subscribe(engine);
        return engine;
    }

    private static BlockingStop Subscribe(AutoClickerEngine engine)
    {
        var stopped = new BlockingStop();
        engine.Stopped += (_, e) => stopped.Set(e);
        return stopped;
    }

    private static EngineStoppedEventArgs WaitForStop(BlockingStop stopped)
    {
        Assert.True(stopped.Wait(Timeout), "The engine did not stop within the timeout.");
        return stopped.Args!;
    }

    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, Timeout);

    /// <summary>Captures the Stopped event so a test can block on it.</summary>
    private sealed class BlockingStop
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

    /// <summary>Stands in for a system that refuses injected input.</summary>
    private sealed class ThrowingInputInjector : IInputInjector
    {
        public string Name => "Throwing";

        public ScreenPoint GetCursorPosition() => ScreenPoint.Zero;

        public void MoveTo(ScreenPoint position) => throw new InputInjectionException("blocked");

        public void MouseButtonDown(MouseButton button) => throw new InputInjectionException("blocked");

        public void MouseButtonUp(MouseButton button) => throw new InputInjectionException("blocked");

        public void KeyDown(VirtualKey key) => throw new InputInjectionException("blocked");

        public void KeyUp(VirtualKey key) => throw new InputInjectionException("blocked");
    }
}
