using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Planning;
using ClickerEngine.Core.Profiles;
using ClickerEngine.Core.Timing;
using Xunit;

namespace ClickerEngine.Core.Tests;

/// <summary>The scenario editor's blocks beyond the first seven: plans, validation, the engine.</summary>
public class ScriptBlocksTests
{
    private static readonly FixedScreenGeometry Screen = new(1920, 1080);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void DoubleClick_IsTwoClicksAndOneAction()
    {
        var profile = ScriptPlannerTests.Script(new PressBlock { Id = "a", Click = ClickType.Double });
        profile.Action.DoubleClickGapMs = 40;

        var steps = Plan(profile);

        Assert.Equal(
            new[] { "MouseDown Left", "MouseUp Left", "Wait 40 ms", "MouseDown Left", "MouseUp Left" },
            steps.Select(s => s.ToString()));
        Assert.Equal(new[] { false, true }, steps.Where(s => s.Kind == EngineStepKind.MouseUp).Select(s => s.CompletesAction));
    }

    [Fact]
    public void ProfilePress_TakesThePressOfTheProfilesClicks()
    {
        var profile = ScriptPlannerTests.Script(new PressBlock { Id = "a", ProfilePress = true, Input = new InputChord(VirtualKey.E) { PressMs = 200 } });
        profile.Action.Inputs = [new InputChord(VirtualKey.Q) { PressMs = 75 }];

        Assert.Equal(75, Plan(profile).Single(s => s.Kind == EngineStepKind.Wait).Delay.TotalMilliseconds);
    }

    [Fact]
    public void HoldFor_PressesWaitsAndReleases_AsOneAction()
    {
        var profile = ScriptPlannerTests.Script(new HoldForBlock { Id = "h", Input = new InputChord(VirtualKey.E, KeyModifiers.Shift), DurationMs = 1500 });

        var steps = Plan(profile);

        Assert.Equal(new[] { "KeyDown LeftShift", "KeyDown E", "Wait 1500 ms", "KeyUp E", "KeyUp LeftShift" }, steps.Select(s => s.ToString()));
        Assert.Single(steps, s => s.CompletesAction);
    }

    [Fact]
    public void Scroll_TurnsTheWheelAtItsPoint()
    {
        var profile = ScriptPlannerTests.Script(
            new ScrollBlock { Id = "down", Notches = 3, At = new ScreenPoint(100, 100) },
            new ScrollBlock { Id = "up", Direction = ScrollDirection.Up, Notches = 2 });
        profile.Script.StepDelayMs = 0;

        Assert.Equal(new[] { "MoveTo (100, 100)", "Scroll -3", "Scroll 2" }, Plan(profile).Select(s => s.ToString()));
    }

    [Fact]
    public void Drag_PressesAtTheStart_CarriesToTheEnd_AndReleases()
    {
        var profile = ScriptPlannerTests.Script(new DragBlock { Id = "d", From = new ScreenPoint(100, 100), To = new ScreenPoint(300, 100), Speed = DragSpeed.Fast });

        var steps = Plan(profile, new ScreenPoint(5, 5));

        Assert.Equal("MoveTo (100, 100)", steps[0].ToString());
        Assert.Equal("MouseDown Left", steps[1].ToString());
        Assert.Equal("MouseUp Left", steps[^1].ToString());
        Assert.Equal("MoveTo (300, 100)", steps[^2].ToString());
        Assert.InRange(steps.Where(s => s.Kind == EngineStepKind.Wait).Sum(s => s.Delay.TotalMilliseconds), 240, 260);
    }

    [Fact]
    public void TypeText_TypesEveryCharacter_AsOneAction()
    {
        var profile = ScriptPlannerTests.Script(new TypeTextBlock { Id = "t", Text = "/tр", CharDelayMs = 20 });

        var steps = Plan(profile);

        Assert.Equal(new[] { "Text '/'", "Wait 20 ms", "Text 't'", "Wait 20 ms", "Text 'р'" }, steps.Select(s => s.ToString()));
        Assert.True(steps[^1].CompletesAction);
        Assert.Equal(ActionKind.Keyboard, steps[0].Device);
    }

    [Fact]
    public void Move_CanBeInstantOrSmoothWhateverTheProfileSays()
    {
        var instant = ScriptPlannerTests.Script(new MoveBlock { Id = "m", To = new ScreenPoint(500, 500), Movement = MovementMode.Instant });
        instant.Movement.Mode = MovementMode.Smooth;
        Assert.Equal(new[] { "MoveTo (500, 500)" }, Plan(instant).Select(s => s.ToString()));

        var smooth = ScriptPlannerTests.Script(new MoveBlock { Id = "m", To = new ScreenPoint(500, 500), Movement = MovementMode.Smooth, DurationMs = 200 });
        var steps = Plan(smooth);
        Assert.True(steps.Count > 2);
        Assert.Equal("MoveTo (500, 500)", steps[^1].ToString());
    }

    [Fact]
    public void Shift_MovesByTheOffsetFromTheCursor()
    {
        var profile = ScriptPlannerTests.Script(new ShiftBlock { Id = "s", Dx = 50, Dy = -20 });

        Assert.Equal(new[] { "MoveTo (150, 80)" }, Plan(profile, new ScreenPoint(100, 100)).Select(s => s.ToString()));
    }

    [Fact]
    public void Random_RunsOneEnabledBlock_AndLabelsDoNothing()
    {
        var profile = ScriptPlannerTests.Script(
            new LabelBlock { Id = "l", Text = "— start —" },
            new RandomBlock
            {
                Id = "r",
                Blocks =
                [
                    new PressBlock { Id = "a", Input = new InputChord(VirtualKey.A) },
                    new PressBlock { Id = "off", Enabled = false, Input = new InputChord(VirtualKey.X) },
                    new PressBlock { Id = "b", Input = new InputChord(VirtualKey.B) },
                ],
            });
        profile.Script.Runs = ScriptRunMode.FixedCount;
        profile.Script.RunCount = 2;
        profile.Script.StepDelayMs = 0;

        var keys = Plan(profile, random: new ScriptedRandomSource(0.0, 0.9))
            .Where(s => s.Kind == EngineStepKind.KeyDown)
            .Select(s => s.Key);

        Assert.Equal(new[] { VirtualKey.A, VirtualKey.B }, keys);
    }

    [Fact]
    public void StartBlock_SkipsWhatComesBefore_AndRunsOnce()
    {
        var profile = ScriptPlannerTests.Script(
            new PressBlock { Id = "a", Input = new InputChord(VirtualKey.A) },
            new RepeatBlock
            {
                Id = "r",
                Count = 2,
                Blocks =
                [
                    new PressBlock { Id = "b", Input = new InputChord(VirtualKey.B) },
                    new PressBlock { Id = "c", Input = new InputChord(VirtualKey.C) },
                ],
            },
            new PressBlock { Id = "d", Input = new InputChord(VirtualKey.D) });
        profile.Script.Runs = ScriptRunMode.Infinite;
        profile.Script.StepDelayMs = 0;
        profile.Script.StartBlockId = "c";

        var keys = Plan(profile).Where(s => s.Kind == EngineStepKind.KeyDown).Select(s => s.Key.ToString());

        // From «c» in the first iteration, then the second iteration whole, then the rest.
        Assert.Equal(new[] { "C", "B", "C", "D" }, keys);
    }

    [Fact]
    public void PlannedActions_CountTheNewBlocks()
    {
        var profile = ScriptPlannerTests.Script(
            new HoldForBlock { Id = "h", Input = new InputChord(VirtualKey.E) },
            new ScrollBlock { Id = "s" },
            new DragBlock { Id = "d" },
            new TypeTextBlock { Id = "t", Text = "hi" },
            new RandomBlock { Id = "r", Blocks = [new PressBlock { Id = "a" }, new ScrollBlock { Id = "s2" }] },
            new LabelBlock { Id = "l" });

        Assert.Equal(5, new ScriptPlanner(profile, new RecordingInputInjector(), Screen).PlannedActionCount);
    }

    [Fact]
    public void Validation_CatchesTheNewBlocksProblems()
    {
        var profile = ScriptPlannerTests.Script(
            new HoldForBlock { Id = "h", Input = null },
            new TypeTextBlock { Id = "t", Text = "" },
            new WaitKeyBlock { Id = "k", Input = null },
            new RandomBlock { Id = "r" },
            new ScrollBlock { Id = "s", Notches = 0 },
            new ReleaseBlock { Id = "rel", Input = new InputChord(VirtualKey.W) });

        var errors = ProfileValidator.Validate(profile);

        Assert.Equal(
            new[] { "h", "t", "k", "r", "s", "rel" },
            errors.Select(e => e.BlockId));
        Assert.EndsWith(".Held", errors[^1].Path);
    }

    [Fact]
    public void Validation_AcceptsAReleaseOfWhatIsHeldAbove()
    {
        var profile = ScriptPlannerTests.Script(
            new HoldBlock { Id = "hold", Input = new InputChord(VirtualKey.W) },
            new RepeatBlock { Id = "r", Count = 2, Blocks = [new ReleaseBlock { Id = "rel", Input = new InputChord(VirtualKey.W) }] });

        Assert.Empty(ProfileValidator.Validate(profile));
    }

    [Fact]
    public void NewBlocks_RoundTripThroughTheProfileFile()
    {
        var profile = ScriptPlannerTests.Script(
            new PressBlock { Id = "p", Click = ClickType.Double, ProfilePress = true },
            new HoldForBlock { Id = "h", Input = new InputChord(VirtualKey.E), RandomizeEnabled = true, MinDurationMs = 100, MaxDurationMs = 200 },
            new ScrollBlock { Id = "s", Direction = ScrollDirection.Up, Notches = 4, At = new ScreenPoint(1, 2) },
            new DragBlock { Id = "d", Button = MouseButton.Right, From = new ScreenPoint(3, 4), To = new ScreenPoint(5, 6), Speed = DragSpeed.Slow },
            new TypeTextBlock { Id = "t", Text = "привет", CharDelayMs = 10 },
            new MoveBlock { Id = "m", To = new ScreenPoint(7, 8), Movement = MovementMode.Smooth, DurationMs = 250 },
            new ShiftBlock { Id = "sh", Dx = -5, Dy = 9 },
            new WaitBlock { Id = "w", InSeconds = true },
            new WaitKeyBlock { Id = "k", Input = new InputChord(VirtualKey.F, KeyModifiers.Control) },
            new RandomBlock { Id = "r", Blocks = [new StopBlock { Id = "x" }] },
            new LabelBlock { Id = "l", Text = "— end —" });

        var json = ProfileSerializer.Serialize(profile);
        var back = ProfileSerializer.Deserialize(json);

        Assert.Equal(json, ProfileSerializer.Serialize(back));
        Assert.DoesNotContain(ScriptSettings.Walk(back.Script.Blocks, enabledOnly: false), b => b is UnknownBlock);
    }

    [Fact]
    public void TheEngine_ScrollsTypesAndWaitsForAKey()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(50, 50));
        var keys = new ManualHotkeyService();
        using var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance) { KeyPresses = keys };
        var stopped = new ManualResetEventSlim(false);
        EngineStoppedEventArgs? result = null;
        engine.Stopped += (_, e) => { result = e; stopped.Set(); };

        var profile = ScriptPlannerTests.Script(
            new ScrollBlock { Id = "s", Notches = 2 },
            new WaitKeyBlock { Id = "k", Input = new InputChord(VirtualKey.Space) },
            new TypeTextBlock { Id = "t", Text = "ok", CharDelayMs = 0 });
        profile.Script.StepDelayMs = 1;
        engine.Profile = profile;

        engine.Start();
        Assert.True(SpinWait.SpinUntil(() => engine.CurrentBlock?.BlockId == "k", Timeout));
        Thread.Sleep(30);
        Assert.False(stopped.IsSet);

        keys.PressKey(VirtualKey.Enter);
        Thread.Sleep(20);
        Assert.False(stopped.IsSet);

        keys.PressKey(VirtualKey.Space);
        Assert.True(stopped.Wait(Timeout));

        Assert.Equal(StopReason.Completed, result!.Reason);
        Assert.Equal(2, result.ActionsPerformed);
        Assert.Equal(new[] { "Scroll -2 @ (50, 50)", "Text 'o'", "Text 'k'" }, injector.Events.Select(e => e.ToString()));
    }

    [Fact]
    public void APause_ReleasesWhatTheScriptHolds_AndResumePressesItAgain()
    {
        var injector = new RecordingInputInjector();
        using var engine = new AutoClickerEngine(injector, new SystemClock(), Screen, NeutralRandomSource.Instance);
        var stopped = new ManualResetEventSlim(false);
        engine.Stopped += (_, _) => stopped.Set();

        engine.Profile = ScriptPlannerTests.Script(
            new HoldBlock { Id = "hold", Input = new InputChord(VirtualKey.W) },
            new WaitBlock { Id = "w", DurationMs = 5000 },
            new ReleaseBlock { Id = "rel", Input = new InputChord(VirtualKey.W) });

        engine.Start();
        Assert.True(SpinWait.SpinUntil(() => engine.CurrentBlock?.BlockId == "w", Timeout));
        Thread.Sleep(20);

        Assert.True(engine.Pause());
        Assert.True(SpinWait.SpinUntil(() => injector.Events.Count(e => e.Kind == InjectedEventKind.KeyUp) == 1, Timeout));

        Assert.True(engine.Resume());
        Assert.True(SpinWait.SpinUntil(() => injector.Events.Count(e => e.Kind == InjectedEventKind.KeyDown) == 2, Timeout));

        engine.Stop();
        Assert.True(stopped.Wait(Timeout));
        Assert.Equal(new[] { "KeyDown W", "KeyUp W", "KeyDown W", "KeyUp W" }, injector.Events.Select(e => e.ToString()));
    }

    private static List<EngineStep> Plan(ClickerProfile profile, ScreenPoint? cursor = null, IRandomSource? random = null)
    {
        var injector = new RecordingInputInjector(cursor ?? new ScreenPoint(10, 10));
        var steps = new List<EngineStep>();
        foreach (var step in new ScriptPlanner(profile, injector, Screen, random).CreatePlan())
        {
            if (step.Kind == EngineStepKind.MoveTo)
            {
                injector.SetCursorPosition(step.Position);
            }

            steps.Add(step);
        }

        return steps;
    }
}
