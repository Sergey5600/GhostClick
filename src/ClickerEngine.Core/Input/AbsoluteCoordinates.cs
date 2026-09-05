using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Input;

/// <summary>
/// Converts virtual-desktop pixels to the normalized 0..65535 range that absolute mouse
/// events use.
/// <para>
/// Pure arithmetic, deliberately kept out of the Win32 class so it can be tested anywhere
/// and reused by any future backend that also speaks absolute coordinates.
/// </para>
/// </summary>
public static class AbsoluteCoordinates
{
    /// <summary>The largest normalized coordinate.</summary>
    public const int Max = 65535;

    /// <summary>
    /// Maps <paramref name="point"/> within <paramref name="bounds"/> onto [0, 65535] on each
    /// axis. The left/top edge maps to 0 and the last addressable pixel maps to 65535, so a
    /// target on the far edge of the desktop is still reachable.
    /// </summary>
    public static (int X, int Y) Normalize(ScreenPoint point, ScreenRect bounds)
    {
        int spanX = Math.Max(1, bounds.Width - 1);
        int spanY = Math.Max(1, bounds.Height - 1);

        double x = (point.X - bounds.Left) * (double)Max / spanX;
        double y = (point.Y - bounds.Top) * (double)Max / spanY;

        return (Clamp(x), Clamp(y));
    }

    private static int Clamp(double value) =>
        (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, Max);
}
