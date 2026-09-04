using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Motion;
using Xunit;

namespace ClickerEngine.Core.Tests;

public class MotionTests
{
    [Theory]
    [InlineData(EasingMode.Linear)]
    [InlineData(EasingMode.EaseIn)]
    [InlineData(EasingMode.EaseOut)]
    [InlineData(EasingMode.EaseInOut)]
    public void EveryEasingCurve_StartsAtZeroAndEndsAtOne(EasingMode mode)
    {
        Assert.Equal(0.0, Easing.Apply(mode, 0.0), 6);
        Assert.Equal(1.0, Easing.Apply(mode, 1.0), 6);
    }

    [Theory]
    [InlineData(EasingMode.Linear)]
    [InlineData(EasingMode.EaseIn)]
    [InlineData(EasingMode.EaseOut)]
    [InlineData(EasingMode.EaseInOut)]
    public void EveryEasingCurve_IsMonotonicAndStaysInRange(EasingMode mode)
    {
        double previous = 0;
        for (int i = 0; i <= 100; i++)
        {
            double value = Easing.Apply(mode, i / 100.0);
            Assert.InRange(value, 0.0, 1.0);
            Assert.True(value >= previous - 1e-9, $"{mode} went backwards at t={i / 100.0}");
            previous = value;
        }
    }

    [Fact]
    public void Easing_ClampsInputOutsideTheUnitInterval()
    {
        Assert.Equal(0.0, Easing.Apply(EasingMode.EaseInOut, -3.0), 6);
        Assert.Equal(1.0, Easing.Apply(EasingMode.EaseInOut, 4.0), 6);
    }

    [Fact]
    public void EaseIn_StartsSlowerThanLinear_AndEaseOut_StartsFaster()
    {
        Assert.True(Easing.Apply(EasingMode.EaseIn, 0.25) < 0.25);
        Assert.True(Easing.Apply(EasingMode.EaseOut, 0.25) > 0.25);
    }

    [Fact]
    public void Plan_ForAZeroLengthMove_ProducesNothing()
    {
        var samples = MotionPlanner.Plan(
            new ScreenPoint(5, 5),
            new ScreenPoint(5, 5),
            new MovementSettings { Mode = MovementMode.Smooth });

        Assert.Empty(samples);
    }

    [Fact]
    public void Plan_InInstantMode_IsASingleJump()
    {
        var samples = MotionPlanner.Plan(
            ScreenPoint.Zero,
            new ScreenPoint(300, 200),
            new MovementSettings { Mode = MovementMode.Instant });

        var sample = Assert.Single(samples);
        Assert.Equal(new ScreenPoint(300, 200), sample.Position);
        Assert.Equal(TimeSpan.Zero, sample.Delay);
    }

    [Fact]
    public void Plan_DerivesDurationFromSpeedWhenNoDurationIsSet()
    {
        var settings = new MovementSettings
        {
            Mode = MovementMode.Smooth,
            DurationMs = 0,
            SpeedPixelsPerSecond = 500,
            StepIntervalMs = 10,
        };

        // 1000 px at 500 px/s is two seconds, i.e. 200 steps of 10 ms.
        var samples = MotionPlanner.Plan(ScreenPoint.Zero, new ScreenPoint(1000, 0), settings);

        Assert.Equal(200, samples.Count);
        Assert.Equal(2000, samples.Sum(s => s.Delay.TotalMilliseconds), 3);
    }

    [Fact]
    public void Plan_RespectsTheStepCeiling()
    {
        var settings = new MovementSettings
        {
            Mode = MovementMode.Smooth,
            DurationMs = 60_000,
            StepIntervalMs = 1,
            MaxSteps = 50,
        };

        var samples = MotionPlanner.Plan(ScreenPoint.Zero, new ScreenPoint(10, 10), settings);

        Assert.Equal(50, samples.Count);
        Assert.Equal(new ScreenPoint(10, 10), samples[^1].Position);
    }

    [Fact]
    public void Plan_AlwaysLandsExactlyOnTheTargetEvenWithPathJitter()
    {
        var settings = new MovementSettings
        {
            Mode = MovementMode.Smooth,
            DurationMs = 100,
            StepIntervalMs = 10,
        };

        var jitter = new JitterSettings { PathJitterPixels = 15 };
        var samples = MotionPlanner.Plan(
            ScreenPoint.Zero,
            new ScreenPoint(200, 100),
            settings,
            jitter,
            new SystemRandomSource(seed: 7));

        Assert.Equal(new ScreenPoint(200, 100), samples[^1].Position);

        // Compared with the same plan without jitter, the intermediate samples must have
        // been pushed off the straight line - but only the intermediate ones.
        var straight = MotionPlanner.Plan(ScreenPoint.Zero, new ScreenPoint(200, 100), settings);

        Assert.Equal(straight.Count, samples.Count);
        Assert.True(
            samples.Take(samples.Count - 1)
                .Where((s, i) => s.Position != straight[i].Position)
                .Any(),
            "Path jitter should have moved at least one intermediate sample.");
    }

    [Fact]
    public void Lerp_RoundsToWholePixels()
    {
        Assert.Equal(new ScreenPoint(5, 5), MotionPlanner.Lerp(ScreenPoint.Zero, new ScreenPoint(10, 10), 0.5));
        Assert.Equal(new ScreenPoint(3, 0), MotionPlanner.Lerp(ScreenPoint.Zero, new ScreenPoint(10, 0), 0.33));
    }

    [Fact]
    public void Walker_TravelsAlongTheRequestedAngle()
    {
        // 90 degrees is up, which on screen means a falling Y.
        var settings = new DirectionalSettings
        {
            AngleDegrees = 90,
            SpeedPixelsPerSecond = 100,
            StepIntervalMs = 10,
        };

        var walker = new DirectionalWalker(new ScreenPoint(500, 500), settings, new ScreenRect(0, 0, 1920, 1080));
        var samples = walker.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(10, samples.Count);
        Assert.All(samples, s => Assert.Equal(500, s.Position.X));
        Assert.Equal(new ScreenPoint(500, 490), samples[^1].Position);
    }

    [Fact]
    public void Walker_AccumulatesSubPixelMovement()
    {
        // 30 px/s in 10 ms steps is 0.3 px per step: without sub-pixel state the cursor would
        // never move at all.
        var settings = new DirectionalSettings
        {
            AngleDegrees = 0,
            SpeedPixelsPerSecond = 30,
            StepIntervalMs = 10,
        };

        var walker = new DirectionalWalker(new ScreenPoint(100, 100), settings, new ScreenRect(0, 0, 1920, 1080));
        walker.Advance(TimeSpan.FromMilliseconds(1000));

        Assert.Equal(new ScreenPoint(130, 100), walker.Position);
    }

    [Fact]
    public void Walker_BouncesOffTheEdge()
    {
        var settings = new DirectionalSettings
        {
            AngleDegrees = 0,
            SpeedPixelsPerSecond = 1000,
            StepIntervalMs = 10,
            OnBounds = BoundsBehavior.Bounce,
        };

        var walker = new DirectionalWalker(new ScreenPoint(1900, 500), settings, new ScreenRect(0, 0, 1920, 1080));
        var samples = walker.Advance(TimeSpan.FromMilliseconds(200));

        Assert.All(samples, s => Assert.InRange(s.Position.X, 0, 1919));
        Assert.False(walker.IsStopped);

        // Having reflected off the right edge, the heading now points left.
        Assert.InRange(walker.AngleDegrees, 179.0, 181.0);
    }

    [Fact]
    public void Walker_StopsAtTheEdgeWhenConfiguredTo()
    {
        var settings = new DirectionalSettings
        {
            AngleDegrees = 180,
            SpeedPixelsPerSecond = 1000,
            StepIntervalMs = 10,
            OnBounds = BoundsBehavior.Stop,
        };

        var walker = new DirectionalWalker(new ScreenPoint(5, 500), settings, new ScreenRect(0, 0, 1920, 1080));
        walker.Advance(TimeSpan.FromMilliseconds(500));

        Assert.True(walker.IsStopped);
        Assert.Equal(0, walker.Position.X);
        Assert.Empty(walker.Advance(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void Walker_WrapsToTheOppositeEdge()
    {
        var settings = new DirectionalSettings
        {
            AngleDegrees = 0,
            SpeedPixelsPerSecond = 1000,
            StepIntervalMs = 10,
            OnBounds = BoundsBehavior.Wrap,
        };

        var walker = new DirectionalWalker(new ScreenPoint(1915, 500), settings, new ScreenRect(0, 0, 1920, 1080));
        walker.Advance(TimeSpan.FromMilliseconds(100));

        Assert.InRange(walker.Position.X, 0, 200);
        Assert.False(walker.IsStopped);
    }

    [Fact]
    public void Walker_RestartsFromItsStartPoint()
    {
        var settings = new DirectionalSettings
        {
            AngleDegrees = 0,
            SpeedPixelsPerSecond = 2000,
            StepIntervalMs = 10,
            OnBounds = BoundsBehavior.Restart,
        };

        var start = new ScreenPoint(1900, 400);
        var walker = new DirectionalWalker(start, settings, new ScreenRect(0, 0, 1920, 1080));
        walker.Advance(TimeSpan.FromMilliseconds(100));

        Assert.InRange(walker.Position.X, start.X - 20, 1919);
        Assert.Equal(400, walker.Position.Y);
    }

    [Fact]
    public void Walker_PerturbsItsAngleWithinTheConfiguredSpread()
    {
        var settings = new DirectionalSettings
        {
            AngleDegrees = 45,
            SpeedPixelsPerSecond = 100,
            StepIntervalMs = 10,
            AngleJitterDegrees = 10,
        };

        var walker = new DirectionalWalker(
            new ScreenPoint(500, 500),
            settings,
            new ScreenRect(0, 0, 1920, 1080),
            new SystemRandomSource(seed: 3));

        walker.PerturbAngle();

        Assert.InRange(walker.AngleDegrees, 35, 55);
        Assert.NotEqual(45.0, walker.AngleDegrees, 3);
    }

    [Fact]
    public void Walker_KeepsItsAngleWhenJitterIsOff()
    {
        var settings = new DirectionalSettings
        {
            AngleDegrees = 45,
            SpeedPixelsPerSecond = 100,
            StepIntervalMs = 10,
            AngleJitterDegrees = 0,
        };

        var walker = new DirectionalWalker(new ScreenPoint(500, 500), settings, new ScreenRect(0, 0, 1920, 1080));
        walker.PerturbAngle();

        Assert.Equal(45.0, walker.AngleDegrees, 3);
    }

    [Fact]
    public void TimingJitter_StaysWithinTheConfiguredPercentage()
    {
        var settings = new JitterSettings
        {
            TimingJitterEnabled = true,
            TimingJitterPercent = 20,
        };

        var random = new SystemRandomSource(seed: 11);
        var baseline = TimeSpan.FromMilliseconds(100);

        for (int i = 0; i < 200; i++)
        {
            var jittered = Jitter.ApplyTiming(baseline, settings, random);
            Assert.InRange(jittered.TotalMilliseconds, 80, 120);
        }
    }

    [Fact]
    public void TimingJitter_IsAnIdentityWhenDisabled()
    {
        var settings = new JitterSettings { TimingJitterEnabled = false, TimingJitterPercent = 50 };
        var duration = TimeSpan.FromMilliseconds(100);

        Assert.Equal(duration, Jitter.ApplyTiming(duration, settings, new SystemRandomSource(seed: 1)));
    }

    [Fact]
    public void PositionJitter_IsAnIdentityWhenDisabled()
    {
        var settings = new JitterSettings { PositionJitterEnabled = false, MaxOffsetX = 50, MaxOffsetY = 50 };
        var point = new ScreenPoint(10, 20);

        Assert.Equal(point, Jitter.ApplyPosition(point, settings, new SystemRandomSource(seed: 1)));
    }

    [Fact]
    public void ScriptedRandomSource_ReplaysItsValuesAndThenLoops()
    {
        var random = new ScriptedRandomSource(0.0, 0.5, 0.999);

        Assert.Equal(0.0, random.NextDouble());
        Assert.Equal(0.5, random.NextDouble());
        Assert.Equal(0.999, random.NextDouble());
        Assert.Equal(0.0, random.NextDouble());

        // 0.5 across [0, 10] must land in the middle, and 0.999 must not overflow the range.
        Assert.Equal(5, random.NextInt(0, 10));
        Assert.Equal(10, random.NextInt(0, 10));
    }
}
