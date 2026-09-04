namespace ClickerEngine.Core.Configuration;

/// <summary>
/// A point in virtual-desktop pixel coordinates. Deliberately not <c>System.Drawing.Point</c>
/// or <c>System.Windows.Point</c> so that Core stays free of UI framework types.
/// </summary>
public readonly record struct ScreenPoint(int X, int Y)
{
    public static readonly ScreenPoint Zero = new(0, 0);

    /// <summary>Euclidean distance to <paramref name="other"/>.</summary>
    public double DistanceTo(ScreenPoint other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public ScreenPoint Offset(int dx, int dy) => new(X + dx, Y + dy);

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>A rectangle in virtual-desktop pixel coordinates.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool Contains(ScreenPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>Clamps <paramref name="point"/> to the last addressable pixel inside the rect.</summary>
    public ScreenPoint Clamp(ScreenPoint point) => new(
        Math.Clamp(point.X, Left, Math.Max(Left, Right - 1)),
        Math.Clamp(point.Y, Top, Math.Max(Top, Bottom - 1)));

    public override string ToString() => $"[{Left},{Top} {Width}x{Height}]";
}
