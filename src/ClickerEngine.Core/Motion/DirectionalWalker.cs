using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Motion;

/// <summary>
/// Drives continuous drift along a heading, as opposed to point-to-point moves. The walker
/// keeps sub-pixel state so a slow drift still advances smoothly instead of getting stuck on
/// rounding, and it knows what to do when it reaches the edge of the desktop.
/// </summary>
public sealed class DirectionalWalker
{
    private readonly DirectionalSettings _settings;
    private readonly ScreenRect _bounds;
    private readonly IRandomSource _random;
    private readonly double _startX;
    private readonly double _startY;

    private double _x;
    private double _y;
    private double _dirX;
    private double _dirY;

    public DirectionalWalker(
        ScreenPoint start,
        DirectionalSettings settings,
        ScreenRect bounds,
        IRandomSource? random = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _bounds = bounds;
        _random = random ?? NeutralRandomSource.Instance;

        var clamped = bounds.Clamp(start);
        _x = _startX = clamped.X;
        _y = _startY = clamped.Y;

        SetAngle(settings.AngleDegrees);
    }

    /// <summary>Current position, rounded to a pixel.</summary>
    public ScreenPoint Position => new(
        (int)Math.Round(_x, MidpointRounding.AwayFromZero),
        (int)Math.Round(_y, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Current heading in degrees: 0 is right, 90 is up. Changes when the walker bounces or
    /// when angle jitter is applied.
    /// </summary>
    public double AngleDegrees
    {
        get
        {
            double degrees = Math.Atan2(-_dirY, _dirX) * 180.0 / Math.PI;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }
    }

    /// <summary>Set once the walker hits an edge while <c>OnBounds</c> is <c>Stop</c>.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>
    /// Produces the samples covering <paramref name="duration"/> of drift. The samples are
    /// spaced by <c>DirectionalSettings.StepIntervalMs</c> and their delays add up to
    /// <paramref name="duration"/>.
    /// </summary>
    public IReadOnlyList<MotionSample> Advance(TimeSpan duration)
    {
        if (IsStopped || duration <= TimeSpan.Zero)
        {
            return Array.Empty<MotionSample>();
        }

        int stepInterval = Math.Max(1, _settings.StepIntervalMs);
        int steps = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / stepInterval));
        var stepDelay = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / steps);
        double stepDistance = _settings.SpeedPixelsPerSecond * stepDelay.TotalSeconds;

        var samples = new List<MotionSample>(steps);
        for (int i = 0; i < steps; i++)
        {
            Step(stepDistance);
            samples.Add(new MotionSample(Position, stepDelay));

            if (IsStopped)
            {
                break;
            }
        }

        return samples;
    }

    /// <summary>
    /// Rotates the heading by a random amount within <c>AngleJitterDegrees</c>. Called
    /// between clicks so the drift wanders instead of tracing a perfect ray.
    /// </summary>
    public void PerturbAngle()
    {
        if (_settings.AngleJitterDegrees <= 0)
        {
            return;
        }

        double delta = ((_random.NextDouble() * 2.0) - 1.0) * _settings.AngleJitterDegrees;
        SetAngle(AngleDegrees + delta);
    }

    private void SetAngle(double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        _dirX = Math.Cos(radians);

        // Screen Y grows downwards, so a positive angle must move the cursor up.
        _dirY = -Math.Sin(radians);
    }

    private void Step(double distance)
    {
        double nextX = _x + (_dirX * distance);
        double nextY = _y + (_dirY * distance);

        double maxX = Math.Max(_bounds.Left, _bounds.Right - 1);
        double maxY = Math.Max(_bounds.Top, _bounds.Bottom - 1);

        bool outX = nextX < _bounds.Left || nextX > maxX;
        bool outY = nextY < _bounds.Top || nextY > maxY;

        if (!outX && !outY)
        {
            _x = nextX;
            _y = nextY;
            return;
        }

        switch (_settings.OnBounds)
        {
            case BoundsBehavior.Stop:
                _x = Math.Clamp(nextX, _bounds.Left, maxX);
                _y = Math.Clamp(nextY, _bounds.Top, maxY);
                IsStopped = true;
                break;

            case BoundsBehavior.Bounce:
                if (outX)
                {
                    _dirX = -_dirX;
                    nextX = Math.Clamp(nextX, _bounds.Left, maxX);
                }

                if (outY)
                {
                    _dirY = -_dirY;
                    nextY = Math.Clamp(nextY, _bounds.Top, maxY);
                }

                _x = nextX;
                _y = nextY;
                break;

            case BoundsBehavior.Wrap:
                _x = outX ? (nextX < _bounds.Left ? maxX : _bounds.Left) : nextX;
                _y = outY ? (nextY < _bounds.Top ? maxY : _bounds.Top) : nextY;
                break;

            case BoundsBehavior.Restart:
                _x = _startX;
                _y = _startY;
                SetAngle(_settings.AngleDegrees);
                break;

            default:
                _x = Math.Clamp(nextX, _bounds.Left, maxX);
                _y = Math.Clamp(nextY, _bounds.Top, maxY);
                break;
        }
    }
}
