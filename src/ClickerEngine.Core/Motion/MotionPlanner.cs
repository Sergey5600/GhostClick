using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Motion;

/// <summary>
/// One step of a cursor move: wait <paramref name="Delay"/>, then place the cursor at
/// <paramref name="Position"/>.
/// </summary>
public readonly record struct MotionSample(ScreenPoint Position, TimeSpan Delay);

/// <summary>Turns "get from A to B" into a concrete list of timed cursor positions.</summary>
public static class MotionPlanner
{
    /// <summary>
    /// Plans a move from <paramref name="from"/> to <paramref name="to"/>.
    /// <para>
    /// Instant movement yields a single sample. Smooth movement yields one sample per
    /// <c>StepIntervalMs</c>, spaced so the whole move takes the configured duration, with
    /// the requested easing curve applied to progress along the line. The final sample is
    /// always the exact target, so easing and path jitter can never leave the cursor short.
    /// </para>
    /// </summary>
    public static IReadOnlyList<MotionSample> Plan(
        ScreenPoint from,
        ScreenPoint to,
        MovementSettings movement,
        JitterSettings? jitter = null,
        IRandomSource? random = null)
    {
        ArgumentNullException.ThrowIfNull(movement);

        if (from == to)
        {
            return Array.Empty<MotionSample>();
        }

        if (movement.Mode == MovementMode.Instant)
        {
            return new[] { new MotionSample(to, TimeSpan.Zero) };
        }

        var duration = ResolveDuration(from, to, movement);
        if (duration <= TimeSpan.Zero)
        {
            return new[] { new MotionSample(to, TimeSpan.Zero) };
        }

        int stepInterval = Math.Max(1, movement.StepIntervalMs);
        int maxSteps = Math.Max(1, movement.MaxSteps);
        int steps = (int)Math.Ceiling(duration.TotalMilliseconds / stepInterval);
        steps = Math.Clamp(steps, 1, maxSteps);

        var stepDelay = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / steps);

        jitter ??= new JitterSettings();
        random ??= NeutralRandomSource.Instance;

        var samples = new List<MotionSample>(steps);
        for (int i = 1; i <= steps; i++)
        {
            double progress = Easing.Apply(movement.Easing, (double)i / steps);
            var position = Lerp(from, to, progress);

            // The last sample must land exactly on the target; jitter only wobbles the path.
            if (i < steps)
            {
                position = Jitter.ApplyPath(position, jitter, random);
            }
            else
            {
                position = to;
            }

            samples.Add(new MotionSample(position, stepDelay));
        }

        return samples;
    }

    /// <summary>Linear interpolation between two pixels, rounded to the nearest pixel.</summary>
    public static ScreenPoint Lerp(ScreenPoint from, ScreenPoint to, double progress)
    {
        double x = from.X + ((to.X - from.X) * progress);
        double y = from.Y + ((to.Y - from.Y) * progress);
        return new ScreenPoint(
            (int)Math.Round(x, MidpointRounding.AwayFromZero),
            (int)Math.Round(y, MidpointRounding.AwayFromZero));
    }

    private static TimeSpan ResolveDuration(ScreenPoint from, ScreenPoint to, MovementSettings movement)
    {
        if (movement.DurationMs > 0)
        {
            return TimeSpan.FromMilliseconds(movement.DurationMs);
        }

        if (movement.SpeedPixelsPerSecond <= 0)
        {
            return TimeSpan.Zero;
        }

        double distance = from.DistanceTo(to);
        return TimeSpan.FromSeconds(distance / movement.SpeedPixelsPerSecond);
    }
}
