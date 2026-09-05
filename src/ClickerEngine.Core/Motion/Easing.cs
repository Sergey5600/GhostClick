using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Motion;

/// <summary>Interpolation curves for smooth cursor movement.</summary>
public static class Easing
{
    /// <summary>
    /// Maps normalized progress <paramref name="t"/> in [0, 1] onto an eased position in
    /// [0, 1]. Every curve satisfies f(0) = 0 and f(1) = 1.
    /// </summary>
    public static double Apply(EasingMode mode, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);

        return mode switch
        {
            EasingMode.Linear => t,
            EasingMode.EaseIn => t * t,
            EasingMode.EaseOut => t * (2.0 - t),
            EasingMode.EaseInOut => t < 0.5 ? 2.0 * t * t : -1.0 + ((4.0 - (2.0 * t)) * t),
            _ => t,
        };
    }
}
