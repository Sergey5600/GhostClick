using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Planning;
using Xunit;

namespace ClickerEngine.Core.Tests;

public class PlannerTests
{
    private static readonly FixedScreenGeometry Screen = new(1920, 1080);

    [Fact]
    public void CurrentCursorMode_ClicksWithoutMovingTheCursor()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 3;
        profile.Interval.DelayMs = 50;

        var steps = Plan(profile, new ScreenPoint(400, 300));

        Assert.DoesNotContain(steps, s => s.Kind == EngineStepKind.MoveTo);
        Assert.Equal(3, steps.Count(s => s.Kind == EngineStepKind.MouseDown));
        Assert.Equal(3, steps.Count(s => s.Kind == EngineStepKind.MouseUp));

        // Two gaps between three clicks, not three: no trailing wait after the last action.
        var waits = steps.Where(s => s.Kind == EngineStepKind.Wait).ToArray();
        Assert.Equal(2, waits.Length);
        Assert.All(waits, w => Assert.Equal(50, w.Delay.TotalMilliseconds));
    }

    [Fact]
    public void FixedCount_ProducesExactlyThatManyActions()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 7;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Equal(7, steps.Count(s => s.CompletesAction));
    }

    [Fact]
    public void InfiniteMode_KeepsProducingSteps()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.Infinite;

        var planner = new ActionPlanner(profile, new RecordingInputInjector(), Screen);

        // A bounded take off an unbounded plan must still deliver: the sequence is lazy and
        // endless, and it is the engine that decides when to stop pulling from it.
        var steps = planner.CreatePlan().Take(500).ToArray();

        Assert.Equal(500, steps.Length);
        Assert.True(steps.Count(s => s.CompletesAction) > 100);
    }

    [Fact]
    public void DoubleClick_EmitsTwoPressesButCountsAsOneAction()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 1;
        profile.Action.ClickType = ClickType.Double;
        profile.Action.DoubleClickGapMs = 25;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Equal(2, steps.Count(s => s.Kind == EngineStepKind.MouseDown));
        Assert.Equal(2, steps.Count(s => s.Kind == EngineStepKind.MouseUp));
        Assert.Equal(1, steps.Count(s => s.CompletesAction));
        Assert.Contains(steps, s => s.Kind == EngineStepKind.Wait && s.Delay.TotalMilliseconds == 25);
    }

    [Fact]
    public void HoldDuration_InsertsAWaitBetweenPressAndRelease()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 1;
        profile.Action.HoldDurationMs = 35;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Collection(
            steps,
            s => Assert.Equal(EngineStepKind.MouseDown, s.Kind),
            s =>
            {
                Assert.Equal(EngineStepKind.Wait, s.Kind);
                Assert.Equal(35, s.Delay.TotalMilliseconds);
            },
            s => Assert.Equal(EngineStepKind.MouseUp, s.Kind));
    }

    [Fact]
    public void FixedPointMode_MovesToTheTargetBeforeClicking()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 1;
        profile.Target.Mode = TargetMode.FixedPoint;
        profile.Target.FixedPoint = new ClickPoint(640, 480);

        var steps = Plan(profile, new ScreenPoint(0, 0));

        var move = Assert.Single(steps, s => s.Kind == EngineStepKind.MoveTo);
        Assert.Equal(new ScreenPoint(640, 480), move.Position);
        Assert.True(
            steps.IndexOf(move) < steps.FindIndex(s => s.Kind == EngineStepKind.MouseDown),
            "The cursor must arrive before the button is pressed.");
    }

    [Fact]
    public void FixedPointMode_SkipsTheMoveWhenTheCursorIsAlreadyThere()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 1;
        profile.Target.Mode = TargetMode.FixedPoint;
        profile.Target.FixedPoint = new ClickPoint(100, 100);

        var steps = Plan(profile, new ScreenPoint(100, 100));

        Assert.DoesNotContain(steps, s => s.Kind == EngineStepKind.MoveTo);
    }

    [Fact]
    public void FixedPointMode_ClampsTargetsToTheDesktop()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 1;
        profile.Target.Mode = TargetMode.FixedPoint;
        profile.Target.FixedPoint = new ClickPoint(99999, -50);

        var steps = Plan(profile, ScreenPoint.Zero);

        var move = Assert.Single(steps, s => s.Kind == EngineStepKind.MoveTo);
        Assert.Equal(new ScreenPoint(1919, 0), move.Position);
    }

    [Fact]
    public void MacroRoute_WalksPointsInOrderAndLoops()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 5;
        profile.Target.Mode = TargetMode.MacroRoute;
        profile.Target.Route =
        [
            new ClickPoint(10, 10),
            new ClickPoint(20, 20),
        ];

        var steps = Plan(profile, ScreenPoint.Zero);
        var visited = steps.Where(s => s.Kind == EngineStepKind.MoveTo).Select(s => s.Position).ToArray();

        Assert.Equal(
            new[]
            {
                new ScreenPoint(10, 10),
                new ScreenPoint(20, 20),
                new ScreenPoint(10, 10),
                new ScreenPoint(20, 20),
                new ScreenPoint(10, 10),
            },
            visited);
    }

    [Fact]
    public void MacroRoute_UsesPerPointDelayAndFallsBackToTheInterval()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 3;
        profile.Interval.DelayMs = 100;
        profile.Target.Mode = TargetMode.MacroRoute;
        profile.Target.Route =
        [
            new ClickPoint(10, 10, delayMs: 250),
            new ClickPoint(20, 20), // no delay of its own -> profile interval
        ];

        var waits = Plan(profile, ScreenPoint.Zero)
            .Where(s => s.Kind == EngineStepKind.Wait)
            .Select(s => s.Delay.TotalMilliseconds)
            .ToArray();

        Assert.Equal(new double[] { 250, 100 }, waits);
    }

    [Fact]
    public void MacroRoute_IgnoresDisabledPoints()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 2;
        profile.Target.Mode = TargetMode.MacroRoute;
        profile.Target.Route =
        [
            new ClickPoint(10, 10),
            new ClickPoint(20, 20) { Enabled = false },
            new ClickPoint(30, 30),
        ];

        var visited = Plan(profile, ScreenPoint.Zero)
            .Where(s => s.Kind == EngineStepKind.MoveTo)
            .Select(s => s.Position)
            .ToArray();

        Assert.Equal(new[] { new ScreenPoint(10, 10), new ScreenPoint(30, 30) }, visited);
    }

    [Fact]
    public void KeyboardMode_WrapsTheKeyPressInItsModifiers()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 1;
        profile.Action.Kind = ActionKind.Keyboard;
        profile.Action.Key = VirtualKey.F;
        profile.Action.KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.Equal(
            new[]
            {
                (EngineStepKind.KeyDown, VirtualKey.LeftControl),
                (EngineStepKind.KeyDown, VirtualKey.LeftShift),
                (EngineStepKind.KeyDown, VirtualKey.F),
                (EngineStepKind.KeyUp, VirtualKey.F),
                (EngineStepKind.KeyUp, VirtualKey.LeftShift),
                (EngineStepKind.KeyUp, VirtualKey.LeftControl),
            },
            steps.Select(s => (s.Kind, s.Key)));

        // Only the real key completes an action; the modifiers are scaffolding.
        var completing = Assert.Single(steps, s => s.CompletesAction);
        Assert.Equal(VirtualKey.F, completing.Key);
    }

    [Fact]
    public void KeyboardMode_EmitsNoMouseInput()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 4;
        profile.Action.Kind = ActionKind.Keyboard;
        profile.Action.Key = VirtualKey.Space;

        var steps = Plan(profile, ScreenPoint.Zero);

        Assert.DoesNotContain(steps, s => s.Kind is EngineStepKind.MouseDown or EngineStepKind.MouseUp);
        Assert.Equal(4, steps.Count(s => s.Kind == EngineStepKind.KeyDown));
    }

    [Fact]
    public void RandomizedInterval_StaysInsideTheConfiguredRange()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 50;
        profile.Interval.RandomizeEnabled = true;
        profile.Interval.MinDelayMs = 80;
        profile.Interval.MaxDelayMs = 120;

        var planner = new ActionPlanner(
            profile,
            new RecordingInputInjector(),
            Screen,
            new SystemRandomSource(seed: 1234));

        var waits = planner.CreatePlan()
            .Where(s => s.Kind == EngineStepKind.Wait)
            .Select(s => s.Delay.TotalMilliseconds)
            .ToArray();

        Assert.Equal(49, waits.Length);
        Assert.All(waits, w => Assert.InRange(w, 80, 120));

        // A range that is actually being sampled, not a constant.
        Assert.True(waits.Distinct().Count() > 5);
    }

    [Fact]
    public void PositionJitter_NudgesTheTargetWithinTheConfiguredBox()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 40;
        profile.Target.Mode = TargetMode.FixedPoint;
        profile.Target.FixedPoint = new ClickPoint(500, 500);
        profile.Jitter.PositionJitterEnabled = true;
        profile.Jitter.MaxOffsetX = 4;
        profile.Jitter.MaxOffsetY = 2;

        var planner = new ActionPlanner(
            profile,
            new RecordingInputInjector(),
            Screen,
            new SystemRandomSource(seed: 99));

        var targets = planner.CreatePlan()
            .Where(s => s.Kind == EngineStepKind.MoveTo)
            .Select(s => s.Position)
            .ToArray();

        Assert.All(targets, p =>
        {
            Assert.InRange(p.X, 496, 504);
            Assert.InRange(p.Y, 498, 502);
        });

        Assert.True(targets.Distinct().Count() > 3, "Jitter should actually vary the target.");
    }

    [Fact]
    public void PositionJitter_AppliesToCurrentCursorModeToo()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 3;
        profile.Jitter.PositionJitterEnabled = true;
        profile.Jitter.MaxOffsetX = 2;
        profile.Jitter.MaxOffsetY = 2;

        var steps = Plan(profile, new ScreenPoint(300, 300), new SystemRandomSource(seed: 5));

        Assert.Equal(3, steps.Count(s => s.Kind == EngineStepKind.MoveTo));
    }

    [Fact]
    public void SmoothMovement_InterpolatesAndLandsExactlyOnTheTarget()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 1;
        profile.Target.Mode = TargetMode.FixedPoint;
        profile.Target.FixedPoint = new ClickPoint(400, 0);
        profile.Movement.Mode = MovementMode.Smooth;
        profile.Movement.Easing = EasingMode.Linear;
        profile.Movement.DurationMs = 100;
        profile.Movement.StepIntervalMs = 10;

        var steps = Plan(profile, ScreenPoint.Zero);
        var moves = steps.Where(s => s.Kind == EngineStepKind.MoveTo).Select(s => s.Position).ToArray();

        Assert.Equal(10, moves.Length);
        Assert.Equal(new ScreenPoint(40, 0), moves[0]);
        Assert.Equal(new ScreenPoint(400, 0), moves[^1]);

        // Each move is preceded by its own wait, and they add up to the requested duration.
        var travelWaits = steps
            .TakeWhile(s => s.Kind != EngineStepKind.MouseDown)
            .Where(s => s.Kind == EngineStepKind.Wait)
            .Sum(s => s.Delay.TotalMilliseconds);

        Assert.Equal(100, travelWaits, 3);
    }

    [Fact]
    public void DirectionalMode_DriftsBetweenClicksInsteadOfTeleporting()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 3;
        profile.Interval.DelayMs = 100;
        profile.Target.Mode = TargetMode.Directional;
        profile.Directional.UseStartPoint = true;
        profile.Directional.StartPoint = new ClickPoint(100, 500);
        profile.Directional.AngleDegrees = 0;      // straight to the right
        profile.Directional.SpeedPixelsPerSecond = 100;
        profile.Directional.StepIntervalMs = 20;

        var steps = Plan(profile, new ScreenPoint(0, 500));
        var moves = steps.Where(s => s.Kind == EngineStepKind.MoveTo).Select(s => s.Position).ToArray();

        // Travelling right at 100 px/s for two 100 ms gaps: 10 px of drift per gap, in 20 ms
        // steps of 2 px each.
        Assert.Equal(new ScreenPoint(100, 500), moves[0]);
        Assert.Equal(new ScreenPoint(120, 500), moves[^1]);
        Assert.All(moves, p => Assert.Equal(500, p.Y));

        // Monotonic travel: every sample is further right than the last.
        for (int i = 1; i < moves.Length; i++)
        {
            Assert.True(moves[i].X >= moves[i - 1].X);
        }

        Assert.Equal(3, steps.Count(s => s.CompletesAction));
    }

    [Fact]
    public void DirectionalMode_StopsAtTheEdgeWhenAskedTo()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Repeat.Mode = RepeatMode.Infinite;
        profile.Interval.DelayMs = 100;
        profile.Target.Mode = TargetMode.Directional;
        profile.Directional.UseStartPoint = true;
        profile.Directional.StartPoint = new ClickPoint(1900, 500);
        profile.Directional.AngleDegrees = 0;
        profile.Directional.SpeedPixelsPerSecond = 1000;
        profile.Directional.OnBounds = BoundsBehavior.Stop;

        // An infinite plan that terminates on its own: the walker hit the edge.
        var steps = Plan(profile, ScreenPoint.Zero).ToArray();

        Assert.True(steps.Length > 0);
        Assert.All(
            steps.Where(s => s.Kind == EngineStepKind.MoveTo),
            s => Assert.InRange(s.Position.X, 0, 1919));
    }

    private static List<EngineStep> Plan(ClickerProfile profile, ScreenPoint cursor, IRandomSource? random = null)
    {
        var injector = new RecordingInputInjector(cursor);
        var planner = new ActionPlanner(profile, injector, Screen, random);

        var steps = new List<EngineStep>();
        foreach (var step in planner.CreatePlan())
        {
            // Keep the simulated cursor in step with the plan, the way the engine would,
            // so "where is the cursor now" questions inside the plan get honest answers.
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
