using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Points;
using ClickerEngine.Core.Timing;
using Xunit;

namespace ClickerEngine.Core.Tests;

public class HotkeyAndRecorderTests
{
    private static readonly FixedScreenGeometry Screen = new(1920, 1080);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void ToggleMode_StartsOnTheFirstPressAndStopsOnTheSecond()
    {
        using var fixture = new ControllerFixture();
        fixture.Profile.Mode = ClickMode.Toggle;
        fixture.Profile.Interval.DelayMs = 5;

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Running));

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Idle));
    }

    [Fact]
    public void ToggleMode_IgnoresTheKeyRelease()
    {
        using var fixture = new ControllerFixture();
        fixture.Profile.Mode = ClickMode.Toggle;
        fixture.Profile.Interval.DelayMs = 5;

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop, isPressed: true);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Running));

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop, isPressed: false);
        Thread.Sleep(50);

        Assert.True(fixture.Engine.IsActive);
        fixture.Engine.Stop();
    }

    [Fact]
    public void HoldMode_RunsOnlyWhileTheKeyIsDown()
    {
        using var fixture = new ControllerFixture();
        fixture.Profile.Mode = ClickMode.Hold;
        fixture.Profile.Interval.DelayMs = 1;

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop, isPressed: true);
        Assert.True(SpinUntil(() => fixture.Engine.Statistics.MouseClicks > 0));

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop, isPressed: false);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Idle));
        Assert.Equal(StopReason.HoldReleased, fixture.LastStop!.Reason);
    }

    [Fact]
    public void EmergencyStop_EndsARunImmediately()
    {
        using var fixture = new ControllerFixture();
        fixture.Profile.Interval.DelayMs = 5;

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Running));

        fixture.Hotkeys.Trigger(HotkeyAction.EmergencyStop);

        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Idle));
        Assert.Equal(StopReason.Failsafe, fixture.LastStop!.Reason);
    }

    [Fact]
    public void EmergencyStop_IsIgnoredWhenTheFailsafeIsTurnedOff()
    {
        using var fixture = new ControllerFixture();
        fixture.Profile.Interval.DelayMs = 5;
        fixture.Profile.Safety.FailsafeEnabled = false;

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Running));

        fixture.Hotkeys.Trigger(HotkeyAction.EmergencyStop);
        Thread.Sleep(50);

        Assert.True(fixture.Engine.IsActive);
        fixture.Engine.Stop();
    }

    [Fact]
    public void PauseHotkey_TogglesPauseAndResume()
    {
        using var fixture = new ControllerFixture();
        fixture.Profile.Interval.DelayMs = 1;

        fixture.Hotkeys.Trigger(HotkeyAction.StartStop);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Running));

        fixture.Hotkeys.Trigger(HotkeyAction.Pause);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Paused));

        fixture.Hotkeys.Trigger(HotkeyAction.Pause);
        Assert.True(SpinUntil(() => fixture.Engine.State == EngineState.Running));

        fixture.Engine.Stop();
    }

    [Fact]
    public void RecordHotkey_CapturesTheCursorPosition()
    {
        using var fixture = new ControllerFixture();
        fixture.Injector.SetCursorPosition(new ScreenPoint(321, 654));

        fixture.Hotkeys.Trigger(HotkeyAction.RecordPoint);

        var point = Assert.Single(fixture.Recorder.Points);
        Assert.Equal(321, point.X);
        Assert.Equal(654, point.Y);
    }

    [Fact]
    public void RecordHotkey_RaisesTheControllerEventWithTheRightIndex()
    {
        using var fixture = new ControllerFixture();
        var recorded = new List<PointRecordedEventArgs>();
        fixture.Controller.PointRecorded += (_, e) => recorded.Add(e);

        fixture.Injector.SetCursorPosition(new ScreenPoint(1, 1));
        fixture.Hotkeys.Trigger(HotkeyAction.RecordPoint);

        fixture.Injector.SetCursorPosition(new ScreenPoint(2, 2));
        fixture.Hotkeys.Trigger(HotkeyAction.RecordPoint);

        Assert.Equal(2, recorded.Count);
        Assert.Equal(0, recorded[0].Index);
        Assert.Equal(1, recorded[1].Index);
        Assert.Equal(2, recorded[1].Point.X);
    }

    [Fact]
    public void Recorder_RemovesAndClearsPoints()
    {
        var injector = new RecordingInputInjector();
        var recorder = new PointRecorder(injector);

        recorder.Record(new ScreenPoint(1, 1));
        recorder.Record(new ScreenPoint(2, 2));
        recorder.Record(new ScreenPoint(3, 3));

        Assert.True(recorder.RemoveAt(1));
        Assert.Equal(new[] { 1, 3 }, recorder.Points.Select(p => p.X));

        Assert.True(recorder.RemoveLast());
        Assert.Equal(new[] { 1 }, recorder.Points.Select(p => p.X));

        recorder.Clear();
        Assert.Empty(recorder.Points);
        Assert.False(recorder.RemoveLast());
        Assert.False(recorder.RemoveAt(0));
    }

    [Fact]
    public void Recorder_StampsTheDefaultDelayOntoNewPoints()
    {
        var recorder = new PointRecorder(new RecordingInputInjector()) { DefaultDelayMs = 500 };

        var point = recorder.Record(new ScreenPoint(7, 8), "checkpoint");

        Assert.Equal(500, point.DelayMs);
        Assert.Equal("checkpoint", point.Label);
    }

    [Fact]
    public void Recorder_CopiesItsPointsIntoAProfileWithoutSharingReferences()
    {
        var recorder = new PointRecorder(new RecordingInputInjector());
        recorder.Record(new ScreenPoint(9, 9));

        var profile = ClickerProfile.CreateDefault();
        recorder.ApplyTo(profile);

        profile.Target.Route[0].X = 111;

        Assert.Equal(9, recorder.Points[0].X);
    }

    [Fact]
    public void Recorder_LoadsAProfileRouteAsACopy()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Target.Route = [new ClickPoint(4, 5, 60)];

        var recorder = new PointRecorder(new RecordingInputInjector());
        recorder.Load(profile.Target.Route);

        recorder.Points[0].X = 999;                 // acting on the returned snapshot
        Assert.Equal(4, profile.Target.Route[0].X);
        Assert.Equal(60, recorder.Points[0].DelayMs);
    }

    [Fact]
    public void HotkeyBinding_MatchesOnlyOnAnExactModifierSet()
    {
        var binding = new HotkeyBinding(VirtualKey.F6, KeyModifiers.Control);

        Assert.True(binding.Matches(VirtualKey.F6, KeyModifiers.Control));
        Assert.False(binding.Matches(VirtualKey.F6, KeyModifiers.None));
        Assert.False(binding.Matches(VirtualKey.F6, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(binding.Matches(VirtualKey.F7, KeyModifiers.Control));
    }

    [Fact]
    public void HotkeyBinding_UnboundNeverMatches()
    {
        var binding = new HotkeyBinding();

        Assert.False(binding.IsBound);
        Assert.False(binding.Matches(VirtualKey.None, KeyModifiers.None));
        Assert.Equal("(none)", binding.ToString());
    }

    [Fact]
    public void HotkeyBinding_FormatsAsAReadableChord()
    {
        Assert.Equal(
            "Ctrl+Shift+F6",
            new HotkeyBinding(VirtualKey.F6, KeyModifiers.Control | KeyModifiers.Shift).ToString());

        Assert.Equal("XButton2", new HotkeyBinding(VirtualKey.XButton2).ToString());
    }

    [Fact]
    public void MouseButtonsAreUsableAsHotkeys()
    {
        var binding = new HotkeyBinding(VirtualKey.XButton1);

        Assert.True(binding.IsMouseBinding);
        Assert.True(binding.Key.TryGetMouseButton(out var button));
        Assert.Equal(MouseButton.XButton1, button);
        Assert.Equal(VirtualKey.XButton1, MouseButton.XButton1.ToVirtualKey());
    }

    [Fact]
    public void ModifierKeysReportTheFlagTheyContribute()
    {
        Assert.Equal(KeyModifiers.Shift, VirtualKey.RightShift.ToModifierFlag());
        Assert.Equal(KeyModifiers.Control, VirtualKey.LeftControl.ToModifierFlag());
        Assert.Equal(KeyModifiers.None, VirtualKey.F6.ToModifierFlag());
        Assert.True(VirtualKey.LeftAlt.IsModifier());
        Assert.False(VirtualKey.A.IsModifier());
    }

    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, Timeout);

    /// <summary>An engine plus a manually driven hotkey service, wired the way the app wires them.</summary>
    private sealed class ControllerFixture : IDisposable
    {
        public ControllerFixture()
        {
            Injector = new RecordingInputInjector();
            Engine = new AutoClickerEngine(Injector, new SystemClock(), Screen, NeutralRandomSource.Instance);
            Engine.Profile = ClickerProfile.CreateDefault();
            Engine.Stopped += (_, e) => LastStop = e;

            Recorder = new PointRecorder(Injector);
            Hotkeys = new ManualHotkeyService();
            Controller = new HotkeyController(Hotkeys, Engine, Recorder);
            Controller.Start();
        }

        public RecordingInputInjector Injector { get; }

        public AutoClickerEngine Engine { get; }

        public PointRecorder Recorder { get; }

        public ManualHotkeyService Hotkeys { get; }

        public HotkeyController Controller { get; }

        public ClickerProfile Profile => Engine.Profile;

        public EngineStoppedEventArgs? LastStop { get; private set; }

        public void Dispose()
        {
            Controller.Dispose();
            Hotkeys.Dispose();
            Engine.Dispose();
        }
    }
}
