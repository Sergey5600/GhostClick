using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Planning;
using Xunit;

namespace ClickerEngine.Core.Tests;

public class ScriptPlannerTests
{
    private static readonly FixedScreenGeometry Screen = new(1920, 1080);

    [Fact]
    public void Blocks_RunTopToBottomWithTheAutomaticPauseBetweenSteps()
    {
        var profile = Script(
            new PressBlock { Id = "click" },
            new MoveBlock { Id = "move", To = new ScreenPoint(640, 480) },
            new PressBlock { Id = "key", Input = new InputChord(VirtualKey.E) });

        var steps = Plan(profile, new ScreenPoint(10, 10));

        Assert.Equal(
            new[]
            {
                "MouseDown Left", "MouseUp Left",
                "Wait 100 ms",
                "MoveTo (640, 480)",
                "Wait 100 ms",
                "KeyDown E", "KeyUp E",
            },
            steps.Select(s => s.ToString()));

        // No pause before the first step or after the last.
        Assert.Equal(EngineStepKind.MouseDown, steps[0].Kind);
        Assert.Equal(EngineStepKind.KeyUp, steps[^1].Kind);
    }

    [Fact]
    public void AWait_ReplacesTheAutomaticPause()
    {
        var profile = Script(
            new PressBlock { Id = "a" },
            new WaitBlock { Id = "w", DurationMs = 0 },
            new PressBlock { Id = "b" },
            new WaitBlock { Id = "long", DurationMs = 1500 },
            new PressBlock { Id = "c" });

        var waits = Plan(profile, ScreenPoint.Zero).Where(s => s.Kind == EngineStepKind.Wait).ToArray();

        // Exactly the two waits the user put there: "Ждать 0 мс" runs a and b back to back.
        Assert.Equal(new[] { 0.0, 1500.0 }, waits.Select(w => w.Delay.TotalMilliseconds));
        Assert.Equal(new[] { "w", "long" }, waits.Select(w => w.Location!.BlockId));
    }

    [Fact]
    public void StepDelay_IsConfigurable_AndZeroMeansNoPause()
    {
        var profile = Script(new PressBlock { Id = "a" }, new PressBlock { Id = "b" });

        profile.Script.StepDelayMs = 250;
        Assert.Equal(250, Assert.Single(Plan(profile, ScreenPoint.Zero), s => s.Kind == EngineStepKind.Wait).Delay.TotalMilliseconds);

        profile.Script.StepDelayMs = 0;
        Assert.DoesNotContain(Plan(profile, ScreenPoint.Zero), s => s.Kind == EngineStepKind.Wait);
    }

    [Fact]
    public void RandomWait_IsDrawnFromItsRange()
    {
        var profile = Script(new WaitBlock { Id = "w", RandomizeEnabled = true, MinDurationMs = 200, MaxDurationMs = 400 });
        profile.Script.Runs = ScriptRunMode.FixedCount;
        profile.Script.RunCount = 3;

        var waits = Plan(profile, ScreenPoint.Zero, new ScriptedRandomSource(0.0, 0.5, 0.999))
            .Select(s => s.Delay.TotalMilliseconds);

        Assert.Equal(new[] { 200.0, 300.0, 400.0 }, waits);
    }

    [Fact]
    public void PressAtAPoint_MovesThereFirst()
    {
        var profile = Script(new PressBlock { Id = "a", At = new ScreenPoint(300, 200) });

        var steps = Plan(profile, new ScreenPoint(5, 5));

        Assert.Equal(new[] { "MoveTo (300, 200)", "MouseDown Left", "MouseUp Left" }, steps.Select(s => s.ToString()));
    }

    [Fact]
    public void Points_UseTheProfilesMovementJitterAndScreenBounds()
    {
        var profile = Script(
            new MoveBlock { Id = "far", To = new ScreenPoint(99999, -50) },
            new PressBlock { Id = "smooth", At = new ScreenPoint(1000, 500) });
        profile.Script.StepDelayMs = 0;
        profile.Movement.Mode = MovementMode.Smooth;
        profile.Movement.DurationMs = 80;
        profile.Movement.StepIntervalMs = 10;

        var moves = Plan(profile, new ScreenPoint(1000, 0)).Where(s => s.Kind == EngineStepKind.MoveTo).ToArray();

        Assert.True(moves.Length > 5, "Smooth movement travels in several steps.");
        Assert.Contains(moves, m => m.Position == new ScreenPoint(1919, 0));
        Assert.Equal(new ScreenPoint(1000, 500), moves[^1].Position);
        Assert.All(moves, m => Assert.True(Screen.VirtualBounds.Contains(m.Position)));
    }

    [Fact]
    public void PositionJitter_NudgesTheBlocksPoint()
    {
        var profile = Script(new MoveBlock { Id = "m", To = new ScreenPoint(500, 500) });
        profile.Jitter.PositionJitterEnabled = true;
        profile.Jitter.MaxOffsetX = 4;
        profile.Jitter.MaxOffsetY = 4;

        var move = Assert.Single(Plan(profile, ScreenPoint.Zero, new ScriptedRandomSource(0.0, 0.999)));

        Assert.Equal(new ScreenPoint(496, 504), move.Position);
    }

    [Fact]
    public void Press_WrapsTheKeyInItsModifiersAndHoldsForItsPressTime()
    {
        var copy = new PressBlock { Id = "copy", Input = new InputChord(VirtualKey.C, KeyModifiers.Control | KeyModifiers.Shift) };
        var profile = Script(copy);
        copy.Input.PressMs = 30;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Equal(
            new[] { "KeyDown LeftControl", "KeyDown LeftShift", "KeyDown C", "Wait 30 ms", "KeyUp C", "KeyUp LeftShift", "KeyUp LeftControl" },
            steps.Select(s => s.ToString()));
        Assert.Equal(1, steps.Count(s => s.CompletesAction));
        Assert.True(steps.Single(s => s.CompletesAction).Key == VirtualKey.C);

        // Every press of the block tells the window gate it is a keyboard action.
        Assert.All(steps.Where(s => s.Kind == EngineStepKind.KeyDown), s => Assert.Equal(ActionKind.Keyboard, s.Device));
        Assert.All(steps.Where(s => s.Key is VirtualKey.LeftControl or VirtualKey.LeftShift), s => Assert.True(s.IsModifier));
    }

    [Fact]
    public void Press_VariesAroundItsPressTimeWithTheProfilesSpread()
    {
        var press = new PressBlock { Id = "p", Input = new InputChord(VirtualKey.E) };
        var profile = Script(press);
        press.Input.PressMs = 100;
        profile.Action.RandomizeHoldDuration = true;
        profile.Action.PressSpreadPercent = 30;

        // 100 ms +/- 30 % at 0.0: 70 ms.
        var steps = Plan(profile, ScreenPoint.Zero, new ScriptedRandomSource(0.0));

        Assert.Equal(new[] { "KeyDown E", "Wait 70 ms", "KeyUp E" }, steps.Select(s => s.ToString()));
    }

    [Fact]
    public void HoldMoveRelease_DragsWithTheButtonDown()
    {
        var profile = Script(
            new HoldBlock { Id = "grab", At = new ScreenPoint(100, 100) },
            new MoveBlock { Id = "drag", To = new ScreenPoint(400, 100) },
            new ReleaseBlock { Id = "drop" });
        profile.Script.StepDelayMs = 50;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Equal(
            new[] { "MoveTo (100, 100)", "MouseDown Left", "Wait 50 ms", "MoveTo (400, 100)", "Wait 50 ms", "MouseUp Left" },
            steps.Select(s => s.ToString()));

        // The drag is one action, completed by the release.
        Assert.True(Assert.Single(steps, s => s.CompletesAction).Kind == EngineStepKind.MouseUp);
        Assert.Equal(ActionKind.Mouse, steps.Single(s => s.Kind == EngineStepKind.MouseDown).Device);
    }

    [Fact]
    public void Move_ToWhereTheCursorAlreadyIsStillProducesAStep()
    {
        var profile = Script(new MoveBlock { Id = "m", To = new ScreenPoint(50, 50) });

        var step = Assert.Single(Plan(profile, new ScreenPoint(50, 50)));

        Assert.Equal(EngineStepKind.MoveTo, step.Kind);
        Assert.Equal("m", step.Location!.BlockId);
    }

    [Fact]
    public void DisabledBlocks_AreSkippedWithEverythingInside()
    {
        var profile = Script(
            new PressBlock { Id = "off", Enabled = false },
            new RepeatBlock { Id = "loop", Enabled = false, Count = 5, Blocks = [new PressBlock { Id = "inner" }] },
            new PressBlock { Id = "on", Input = new InputChord(VirtualKey.Q) });

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Equal(new[] { "KeyDown Q", "KeyUp Q" }, steps.Select(s => s.ToString()));
    }

    [Fact]
    public void Repeat_RunsItsBlocksThatManyTimesAndReportsTheIteration()
    {
        var profile = Script(
            new RepeatBlock
            {
                Id = "outer",
                Count = 2,
                Blocks =
                [
                    new PressBlock { Id = "a" },
                    new RepeatBlock { Id = "inner", Count = 3, Blocks = [new PressBlock { Id = "b", Input = new InputChord(VirtualKey.B) }] },
                ],
            });

        var starts = BlockStarts(Plan(profile, ScreenPoint.Zero));

        Assert.Equal(
            new[]
            {
                "a outer 1/2", "b outer 1/2 inner 1/3", "b outer 1/2 inner 2/3", "b outer 1/2 inner 3/3",
                "a outer 2/2", "b outer 2/2 inner 1/3", "b outer 2/2 inner 2/3", "b outer 2/2 inner 3/3",
            },
            starts.Select(l => string.Join(" ", new[] { l.BlockId }.Concat(l.Loops.Select(p => $"{p.BlockId} {p.Iteration}/{p.Count}")))));
    }

    [Fact]
    public void InfiniteRepeat_KeepsProducingSteps()
    {
        var profile = Script(new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new PressBlock { Id = "a" }] });

        var steps = new ScriptPlanner(profile, new RecordingInputInjector(), Screen).CreatePlan().Take(3000).ToArray();

        Assert.Equal(3000, steps.Length);
        Assert.True(steps.Count(s => s.CompletesAction) > 900);
        Assert.All(steps.Where(s => s.Location is not null), s => Assert.Null(s.Location!.Loops[0].Count));
    }

    [Fact]
    public void Stop_EndsTheWholeRunIncludingLaterPasses()
    {
        var profile = Script(
            new PressBlock { Id = "a" },
            new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new PressBlock { Id = "b" }, new StopBlock { Id = "stop" }] },
            new PressBlock { Id = "never" });
        profile.Script.Runs = ScriptRunMode.Infinite;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Equal(new[] { "a", "b" }, BlockStarts(steps).Select(l => l.BlockId));
        Assert.Equal(2, steps.Count(s => s.CompletesAction));
    }

    [Theory]
    [InlineData(ScriptRunMode.Once, 5, 1)]
    [InlineData(ScriptRunMode.FixedCount, 4, 4)]
    [InlineData(ScriptRunMode.FixedCount, 0, 1)]
    public void Runs_RepeatTheWholeScript(ScriptRunMode runs, int runCount, int expectedPasses)
    {
        var profile = Script(new PressBlock { Id = "a" }, new PressBlock { Id = "b" });
        profile.Script.Runs = runs;
        profile.Script.RunCount = runCount;

        var starts = BlockStarts(Plan(profile, ScreenPoint.Zero));

        Assert.Equal(expectedPasses * 2, starts.Count);
        Assert.Equal(Enumerable.Range(1, expectedPasses).SelectMany(p => new long[] { p, p }), starts.Select(l => l.Pass));
        Assert.All(starts, l => Assert.Equal(expectedPasses, l.Passes));
    }

    [Fact]
    public void InfiniteRuns_KeepGoingWithThePauseBetweenPasses()
    {
        var profile = Script(new PressBlock { Id = "a" });
        profile.Script.Runs = ScriptRunMode.Infinite;

        var steps = new ScriptPlanner(profile, new RecordingInputInjector(), Screen).CreatePlan().Take(30).ToArray();

        Assert.Equal(new[] { "MouseDown Left", "MouseUp Left", "Wait 100 ms" }, steps.Take(3).Select(s => s.ToString()));
        Assert.Equal(10, steps.Count(s => s.CompletesAction));
        Assert.Null(steps[0].Location!.Passes);
        Assert.Equal(10, steps[^2].Location!.Pass);
    }

    [Fact]
    public void EveryStepOfABlockCarriesItsLocation_ThePauseCarriesNone()
    {
        var profile = Script(
            new PressBlock { Id = "a", At = new ScreenPoint(10, 10) },
            new RepeatBlock { Id = "r", Count = 2, Blocks = [new PressBlock { Id = "b" }] });

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.All(steps.Where(s => s.Kind != EngineStepKind.Wait), s => Assert.NotNull(s.Location));
        Assert.All(steps.Where(s => s.Kind == EngineStepKind.Wait), s => Assert.Null(s.Location));

        // One location per execution: both runs of b are told apart, a's steps share one.
        Assert.Single(steps.Where(s => s.Location?.BlockId == "a").Select(s => s.Location).Distinct());
        Assert.Equal(2, steps.Where(s => s.Location?.BlockId == "b").Select(s => s.Location).Distinct().Count());
    }

    [Fact]
    public void ARepeatWithNothingToRun_EndsInsteadOfSpinning()
    {
        // The validator refuses these; the planner still must not hang if handed one.
        var profile = Script(new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new PressBlock { Id = "a", Enabled = false }] });
        profile.Script.Runs = ScriptRunMode.Infinite;

        Assert.Empty(new ScriptPlanner(profile, new RecordingInputInjector(), Screen).CreatePlan());
    }

    [Fact]
    public void UnknownBlocks_AreSkipped()
    {
        var profile = Script(new UnknownBlock("waitPixel") { Id = "u" }, new PressBlock { Id = "a" });

        Assert.Equal(new[] { "a" }, BlockStarts(Plan(profile, ScreenPoint.Zero)).Select(l => l.BlockId));
    }

    [Fact]
    public void EditingTheProfileMidRunDoesNotChangeThePlan()
    {
        var profile = Script(new PressBlock { Id = "a" }, new PressBlock { Id = "b" });
        var plan = new ScriptPlanner(profile, new RecordingInputInjector(), Screen).CreatePlan().GetEnumerator();

        Assert.True(plan.MoveNext());
        profile.Script.Blocks.Clear();

        var rest = new List<EngineStep>();
        while (plan.MoveNext())
        {
            rest.Add(plan.Current);
        }

        Assert.Contains(rest, s => s.Location?.BlockId == "b");
    }

    [Fact]
    public void PlannedActionCount_CountsPressesAndReleasesAcrossRepeatsAndPasses()
    {
        var profile = Script(
            new PressBlock { Id = "a" },
            new HoldBlock { Id = "h" },
            new ReleaseBlock { Id = "r" },
            new WaitBlock { Id = "w" },
            new PressBlock { Id = "off", Enabled = false },
            new RepeatBlock { Id = "loop", Count = 3, Blocks = [new PressBlock { Id = "b" }, new MoveBlock { Id = "m" }] });
        profile.Script.Runs = ScriptRunMode.FixedCount;
        profile.Script.RunCount = 2;

        var planner = new ScriptPlanner(profile, new RecordingInputInjector(), Screen);

        // (press + release + 3 presses) per pass, two passes - and the plan agrees.
        Assert.Equal(10, planner.PlannedActionCount);
        Assert.Equal(10, Plan(profile, ScreenPoint.Zero).Count(s => s.CompletesAction));
    }

    [Fact]
    public void PlannedActionCount_StopsAtAStopAndIsUnknownForEndlessScripts()
    {
        var stops = Script(new PressBlock { Id = "a" }, new RepeatBlock { Id = "r", Count = 9, Blocks = [new PressBlock { Id = "b" }, new StopBlock { Id = "s" }] });
        stops.Script.Runs = ScriptRunMode.Infinite;
        Assert.Equal(2, new ScriptPlanner(stops, new RecordingInputInjector(), Screen).PlannedActionCount);

        var endlessRuns = Script(new PressBlock { Id = "a" });
        endlessRuns.Script.Runs = ScriptRunMode.Infinite;
        Assert.Null(new ScriptPlanner(endlessRuns, new RecordingInputInjector(), Screen).PlannedActionCount);

        var endlessRepeat = Script(new RepeatBlock { Id = "r", Mode = RepeatMode.Infinite, Blocks = [new PressBlock { Id = "a" }] });
        Assert.Null(new ScriptPlanner(endlessRepeat, new RecordingInputInjector(), Screen).PlannedActionCount);
    }

    [Fact]
    public void RunPlanner_PicksThePlannerForTheProfilesKind()
    {
        var profile = ClickerProfile.CreateDefault();
        var cursor = new RecordingInputInjector();

        Assert.IsType<ActionPlanner>(RunPlanner.For(profile, cursor, Screen));

        profile.Kind = ProfileKind.Script;
        Assert.IsType<ScriptPlanner>(RunPlanner.For(profile, cursor, Screen));
    }

    /// <summary>
    /// A script profile, run once, with the default 100 ms pause and «Нажать» blocks that
    /// release at once, so a test about order and pauses is not about press times too.
    /// </summary>
    internal static ClickerProfile Script(params ScriptBlock[] blocks)
    {
        var profile = ClickerProfile.CreateDefault("Script");
        profile.Kind = ProfileKind.Script;
        profile.Script.Blocks = blocks.ToList();

        foreach (var press in ScriptSettings.Walk(profile.Script.Blocks, enabledOnly: false).OfType<PressBlock>())
        {
            if (press.Input is { } input)
            {
                input.PressMs = 0;
            }
        }

        return profile;
    }

    /// <summary>The location of the first step of each block execution, in order.</summary>
    private static List<ScriptLocation> BlockStarts(IEnumerable<EngineStep> steps) =>
        steps.Select(s => s.Location).Where(l => l is not null).Distinct().ToList()!;

    private static List<EngineStep> Plan(ClickerProfile profile, ScreenPoint cursor, IRandomSource? random = null)
    {
        var injector = new RecordingInputInjector(cursor);
        var planner = new ScriptPlanner(profile, injector, Screen, random);

        var steps = new List<EngineStep>();
        foreach (var step in planner.CreatePlan())
        {
            // Keep the simulated cursor in step with the plan, the way the engine would.
            if (step.Kind == EngineStepKind.MoveTo)
            {
                injector.SetCursorPosition(step.Position);
            }

            steps.Add(step);

            if (steps.Count > 100_000)
            {
                break;
            }
        }

        return steps;
    }
}
