using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Planning;
using ClickerEngine.Core.Timing;
using Xunit;

namespace ClickerEngine.Core.Tests;

/// <summary>Script profiles run by the engine: the same run loop, limits and gates as a clicker.</summary>
public class ScriptEngineTests
{
    private const long OwnWindow = 1;
    private const long GameWindow = 2;

    private static readonly FixedScreenGeometry Screen = new(1920, 1080);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AScript_RunsTopToBottomAndCompletes()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(50, 50));
        using var engine = CreateEngine(injector, out var stopped);
        engine.Profile = Script(
            new PressBlock { Id = "click", At = new ScreenPoint(300, 300) },
            new WaitBlock { Id = "w", DurationMs = 1 },
            new PressBlock { Id = "key", Input = new InputChord(VirtualKey.E) },
            new MoveBlock { Id = "away", To = new ScreenPoint(10, 10) });
        engine.Profile.Target.RestoreCursorOnStop = false;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.Completed, args.Reason);
        Assert.Equal(2, args.ActionsPerformed);
        Assert.Equal(1, engine.Statistics.MouseClicks);
        Assert.Equal(1, engine.Statistics.KeyPresses);
        Assert.Equal(
            new[] { "Move (300, 300)", "MouseDown Left @ (300, 300)", "MouseUp Left @ (300, 300)", "KeyDown E", "KeyUp E", "Move (10, 10)" },
            injector.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void BlockStarted_ReportsEachBlockAsItStarts_AndTheCurrentBlockClearsAtTheEnd()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        engine.Profile = Script(
            new PressBlock { Id = "a" },
            new RepeatBlock { Id = "r", Count = 2, Blocks = [new WaitBlock { Id = "w", DurationMs = 1 }, new PressBlock { Id = "b" }] },
            new PressBlock { Id = "off", Enabled = false });

        var started = new List<ScriptLocation>();
        var currentWhileRunning = new List<ScriptLocation>();
        engine.BlockStarted += (_, e) =>
        {
            lock (started)
            {
                started.Add(e.Location);
                currentWhileRunning.Add(engine.CurrentBlock!);
            }
        };

        engine.Start();
        WaitForStop(stopped);

        lock (started)
        {
            Assert.Equal(new[] { "a", "w", "b", "w", "b" }, started.Select(l => l.BlockId));
            Assert.Equal(new long?[] { null, 1, 1, 2, 2 }, started.Select(l => l.Loops.Count == 0 ? (long?)null : l.Loops[0].Iteration));
            Assert.Equal(started, currentWhileRunning);
        }

        Assert.Null(engine.CurrentBlock);
    }

    [Fact]
    public void AClicker_NeverReportsBlocks()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 3;
        profile.Interval.DelayMs = 1;
        engine.Profile = profile;

        int reports = 0;
        engine.BlockStarted += (_, _) => Interlocked.Increment(ref reports);

        engine.Start();
        WaitForStop(stopped);

        Assert.Equal(0, reports);
    }

    [Fact]
    public void Started_CarriesThePlannedActionsOfTheScript()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        engine.Profile = Script(new RepeatBlock { Id = "r", Count = 4, Blocks = [new PressBlock { Id = "a" }] });

        long? planned = -1;
        engine.Started += (_, e) => planned = e.PlannedActions;

        engine.Start();
        Assert.Equal(4, WaitForStop(stopped).ActionsPerformed);
        Assert.Equal(4, planned);
    }

    [Fact]
    public void AStopBlock_CompletesTheRun()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        var profile = Script(new PressBlock { Id = "a" }, new StopBlock { Id = "stop" }, new PressBlock { Id = "never" });
        profile.Script.Runs = ScriptRunMode.Infinite;
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.Completed, args.Reason);
        Assert.Equal(1, args.ActionsPerformed);
    }

    [Fact]
    public void AnInvalidScript_DoesNotStart_AndSaysWhichBlock()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out _);
        engine.Profile = Script(new PressBlock { Id = "a" }, new RepeatBlock { Id = "broken", Count = 0, Blocks = [new PressBlock { Id = "b" }] });

        var ex = Assert.Throws<ProfileValidationException>(() => engine.Start());

        Assert.Equal("broken", Assert.Single(ex.Errors).BlockId);
        Assert.Equal(EngineState.Idle, engine.State);
    }

    [Fact]
    public void EmergencyStop_EndsAnEndlessScriptAndReleasesWhatItHeld()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        engine.Profile = Script(
            new HoldBlock { Id = "grab", Input = InputChord.Mouse(MouseButton.Left) },
            new HoldBlock { Id = "run", Input = new InputChord(VirtualKey.W, KeyModifiers.Shift) },
            new RepeatBlock { Id = "forever", Mode = RepeatMode.Infinite, Blocks = [new WaitBlock { Id = "w", DurationMs = 5 }] });

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentBlock?.BlockId == "w"));

        engine.EmergencyStop();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.Failsafe, args.Reason);
        foreach (var down in injector.Events.Where(e => e.Kind is InjectedEventKind.MouseDown or InjectedEventKind.KeyDown))
        {
            Assert.Contains(injector.Events, up =>
                (up.Kind == InjectedEventKind.MouseUp && down.Kind == InjectedEventKind.MouseDown && up.Button == down.Button) ||
                (up.Kind == InjectedEventKind.KeyUp && down.Kind == InjectedEventKind.KeyDown && up.Key == down.Key));
        }

        Assert.Equal(3, injector.Events.Count(e => e.Kind is InjectedEventKind.MouseUp or InjectedEventKind.KeyUp));
    }

    [Fact]
    public void TheActionLimit_StopsAScript()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        var profile = Script(new PressBlock { Id = "a" });
        profile.Script.Runs = ScriptRunMode.Infinite;
        profile.Script.StepDelayMs = 1;
        profile.Safety.MaxActionsPerRun = 5;
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.ActionLimit, args.Reason);
        Assert.Equal(5, args.ActionsPerformed);
    }

    [Fact]
    public void TheTimeLimit_StopsAScript()
    {
        var injector = new RecordingInputInjector();
        var clock = new VirtualClock();
        using var engine = new AutoClickerEngine(injector, clock, Screen, NeutralRandomSource.Instance);
        var stopped = Subscribe(engine);

        var profile = Script(new PressBlock { Id = "a" }, new WaitBlock { Id = "w", DurationMs = 1000 });
        profile.Script.Runs = ScriptRunMode.Infinite;
        profile.Safety.MaxRunDurationSeconds = 3;
        engine.Profile = profile;

        engine.Start();
        var args = WaitForStop(stopped);

        Assert.Equal(StopReason.TimeLimit, args.Reason);
        Assert.Equal(3, args.ActionsPerformed);
    }

    [Fact]
    public void AScriptThatOnlyTypes_IsJudgedByTheForegroundWindowNotTheCursor()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = OwnWindow, Foreground = GameWindow };
        using var engine = CreateEngine(injector, out var stopped, probe);
        engine.Profile = Script(new PressBlock { Id = "a", Input = new InputChord(VirtualKey.Space) });

        engine.Start();

        Assert.Equal(StopReason.Completed, WaitForStop(stopped).Reason);
        Assert.Contains(injector.Events, e => e.Kind == InjectedEventKind.KeyDown && e.Key == VirtualKey.Space);
    }

    [Fact]
    public void AScriptsClicks_AreHeldWhileTheyWouldLandOnTheClickerItself()
    {
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = OwnWindow, Foreground = GameWindow };
        using var engine = CreateEngine(injector, out var stopped, probe);
        engine.Profile = Script(new PressBlock { Id = "a", Input = new InputChord(VirtualKey.E) }, new PressBlock { Id = "click" });

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentBlock?.BlockId == "click"));
        Thread.Sleep(80);

        // The key went to the game; the click waits for the cursor to leave the clicker.
        Assert.DoesNotContain(injector.Events, e => e.Kind == InjectedEventKind.MouseDown);
        Assert.Equal(EngineState.Running, engine.State);

        probe.UnderCursor = GameWindow;
        Assert.Equal(StopReason.Completed, WaitForStop(stopped).Reason);
        Assert.Contains(injector.Events, e => e.Kind == InjectedEventKind.MouseDown);
    }

    [Fact]
    public void AClickWhileAKeyIsHeld_StillWaitsForTheWindow()
    {
        // «Зажать Shift» opens an action that only its «Отпустить» closes; the click in between
        // is a block of its own and must not slip past the gate inside it.
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = OwnWindow, Foreground = GameWindow };
        using var engine = CreateEngine(injector, out var stopped, probe);
        engine.Profile = Script(
            new HoldBlock { Id = "shift", Input = new InputChord(VirtualKey.LeftShift) },
            new PressBlock { Id = "click" },
            new ReleaseBlock { Id = "unshift", Input = new InputChord(VirtualKey.LeftShift) });

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentBlock?.BlockId == "click"));
        Thread.Sleep(80);

        Assert.Contains(injector.Events, e => e.Kind == InjectedEventKind.KeyDown && e.Key == VirtualKey.LeftShift);
        Assert.DoesNotContain(injector.Events, e => e.Kind == InjectedEventKind.MouseDown);

        probe.UnderCursor = GameWindow;
        Assert.Equal(StopReason.Completed, WaitForStop(stopped).Reason);
        Assert.Contains(injector.Events, e => e.Kind == InjectedEventKind.MouseDown);
    }

    [Fact]
    public void AHeldKey_DoesNotLetLaterBlocksPastTheWindowGate()
    {
        // Every click of an endless repeat inside a held key passes the gate on its own.
        var injector = new RecordingInputInjector();
        var probe = new FakeWindowProbe { UnderCursor = GameWindow, Foreground = GameWindow };
        using var engine = CreateEngine(injector, out var stopped, probe);
        engine.Profile = Script(
            new HoldBlock { Id = "walk", Input = new InputChord(VirtualKey.W) },
            new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new PressBlock { Id = "click" }] });

        int Clicks() => injector.Events.Count(e => e.Kind == InjectedEventKind.MouseDown);

        engine.Start();
        Assert.True(SpinUntil(() => Clicks() >= 3));

        probe.UnderCursor = OwnWindow;
        Thread.Sleep(60);
        int heldAt = Clicks();
        Thread.Sleep(150);

        Assert.Equal(heldAt, Clicks());
        Assert.Equal(EngineState.Running, engine.State);

        probe.UnderCursor = GameWindow;
        Assert.True(SpinUntil(() => Clicks() > heldAt + 3));

        engine.Stop();
        WaitForStop(stopped);
        Assert.Contains(injector.Events, e => e.Kind == InjectedEventKind.KeyUp && e.Key == VirtualKey.W);
    }

    [Fact]
    public void StopOnUserCursorMove_WatchesThePointTheScriptMovedTo()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(100, 100));
        using var engine = CreateEngine(injector, out var stopped);
        var profile = Script(
            new MoveBlock { Id = "m", To = new ScreenPoint(500, 500) },
            new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new PressBlock { Id = "a" }] });
        profile.Script.StepDelayMs = 2;
        profile.Safety.StopOnUserCursorMove = true;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentRunActions >= 2));

        injector.SetCursorPosition(new ScreenPoint(900, 900));

        Assert.Equal(StopReason.UserCursorMoved, WaitForStop(stopped).Reason);
    }

    [Fact]
    public void ActionExecuted_ReportsTheKindOfTheBlockThatCompletedIt()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        engine.Profile = Script(new PressBlock { Id = "key", Input = new InputChord(VirtualKey.E) }, new PressBlock { Id = "click" });

        var kinds = new List<ActionKind>();
        engine.ActionExecuted += (_, e) =>
        {
            lock (kinds)
            {
                kinds.Add(e.Kind);
            }
        };

        engine.Start();
        WaitForStop(stopped);

        lock (kinds)
        {
            Assert.Equal(new[] { ActionKind.Keyboard, ActionKind.Mouse }, kinds);
        }
    }

    [Fact]
    public void TheStartHotkey_TogglesAScript()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        var profile = Script(new PressBlock { Id = "a" });
        profile.Script.Runs = ScriptRunMode.Infinite;
        profile.Script.StepDelayMs = 2;
        engine.Profile = profile;

        using var hotkeys = new ManualHotkeyService();
        using var controller = new HotkeyController(hotkeys, engine);
        controller.Start();

        hotkeys.Trigger(HotkeyAction.StartStop);
        Assert.True(SpinUntil(() => engine.CurrentRunActions > 2));

        hotkeys.Trigger(HotkeyAction.StartStop);
        Assert.Equal(StopReason.User, WaitForStop(stopped).Reason);
    }

    [Fact]
    public void PauseAndResume_HoldTheScriptAtABlock()
    {
        var injector = new RecordingInputInjector();
        using var engine = CreateEngine(injector, out var stopped);
        var profile = Script(new PressBlock { Id = "a" });
        profile.Script.Runs = ScriptRunMode.Infinite;
        profile.Script.StepDelayMs = 1;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinUntil(() => engine.CurrentRunActions > 0));
        Assert.True(engine.Pause());
        Thread.Sleep(20);

        long paused = engine.CurrentRunActions;
        Thread.Sleep(60);
        Assert.Equal(paused, engine.CurrentRunActions);
        Assert.Equal("a", engine.CurrentBlock?.BlockId);

        Assert.True(engine.Resume());
        Assert.True(SpinUntil(() => engine.CurrentRunActions > paused));

        engine.Stop();
        Assert.Equal(StopReason.User, WaitForStop(stopped).Reason);
    }

    private static ClickerProfile Script(params ScriptBlock[] blocks)
    {
        var profile = ScriptPlannerTests.Script(blocks);
        profile.Script.StepDelayMs = 1;
        return profile;
    }

    private static AutoClickerEngine CreateEngine(IInputInjector injector, out StopSignal stopped, IWindowProbe? probe = null)
    {
        var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance, probe)
        {
            WindowPollInterval = TimeSpan.FromMilliseconds(2),
            WindowSettleTime = TimeSpan.FromMilliseconds(10),
        };

        stopped = Subscribe(engine);
        return engine;
    }

    private static StopSignal Subscribe(AutoClickerEngine engine)
    {
        var stopped = new StopSignal();
        engine.Stopped += (_, e) => stopped.Set(e);
        return stopped;
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

        public long GetForegroundWindow() => Foreground;

        public long GetWindowAt(ScreenPoint point) => UnderCursor;

        public bool IsOwnWindow(long window) => window == OwnWindow;
    }
}
