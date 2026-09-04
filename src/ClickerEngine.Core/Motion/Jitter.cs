using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Motion;

/// <summary>
/// Randomization helpers shared by the planner and the motion code.
/// <para>
/// This makes the emitted stream look less machine-perfect. It does not, and cannot, hide
/// the fact that the input is synthetic from a driver- or kernel-level observer.
/// </para>
/// </summary>
public static class Jitter
{
    /// <summary>
    /// Offsets a click target by up to +/- <c>MaxOffsetX</c> / <c>MaxOffsetY</c> pixels.
    /// Returns the point unchanged when position jitter is off.
    /// </summary>
    public static ScreenPoint ApplyPosition(ScreenPoint point, JitterSettings settings, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(random);

        if (!settings.PositionJitterEnabled)
        {
            return point;
        }

        int dx = Symmetric(settings.MaxOffsetX, random);
        int dy = Symmetric(settings.MaxOffsetY, random);
        return point.Offset(dx, dy);
    }

    /// <summary>Offsets an intermediate path sample by up to +/- <c>PathJitterPixels</c>.</summary>
    public static ScreenPoint ApplyPath(ScreenPoint point, JitterSettings settings, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(random);

        if (settings.PathJitterPixels <= 0)
        {
            return point;
        }

        int dx = Symmetric(settings.PathJitterPixels, random);
        int dy = Symmetric(settings.PathJitterPixels, random);
        return point.Offset(dx, dy);
    }

    /// <summary>
    /// Scales a wait by 1 +/- <c>TimingJitterPercent</c>. Never returns a negative duration.
    /// </summary>
    public static TimeSpan ApplyTiming(TimeSpan duration, JitterSettings settings, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(random);

        if (!settings.TimingJitterEnabled || duration <= TimeSpan.Zero || settings.TimingJitterPercent <= 0)
        {
            return duration;
        }

        double spread = Math.Clamp(settings.TimingJitterPercent, 0, 100) / 100.0;
        double factor = 1.0 + (((random.NextDouble() * 2.0) - 1.0) * spread);
        double ms = duration.TotalMilliseconds * factor;
        return TimeSpan.FromMilliseconds(Math.Max(0, ms));
    }

    /// <summary>A uniform integer in [-magnitude, +magnitude].</summary>
    private static int Symmetric(int magnitude, IRandomSource random) =>
        magnitude <= 0 ? 0 : random.NextInt(-magnitude, magnitude);
}
