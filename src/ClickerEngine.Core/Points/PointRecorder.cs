using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;

namespace ClickerEngine.Core.Points;

/// <summary>Reports a captured point.</summary>
public sealed class PointRecordedEventArgs : EventArgs
{
    public PointRecordedEventArgs(ClickPoint point, int index)
    {
        Point = point;
        Index = index;
    }

    public ClickPoint Point { get; }

    /// <summary>Position of the new point in the route.</summary>
    public int Index { get; }
}

/// <summary>
/// Implements "hover, press the record hotkey, the coordinate is saved".
/// <para>
/// This is the capture logic only - it owns the list of points and knows how to read the
/// cursor. Whatever UI comes later just binds to <see cref="Points"/> and calls
/// <see cref="Record(string?)"/>; the hotkey path is already wired up by <c>HotkeyController</c>.
/// </para>
/// </summary>
public sealed class PointRecorder
{
    private readonly ICursorProvider _cursor;
    private readonly List<ClickPoint> _points = new();
    private readonly object _sync = new();

    public PointRecorder(ICursorProvider cursor) =>
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));

    /// <summary>Raised after every capture.</summary>
    public event EventHandler<PointRecordedEventArgs>? PointRecorded;

    /// <summary>Raised whenever the list changes, capture included.</summary>
    public event EventHandler? PointsChanged;

    /// <summary>The captured points, in order.</summary>
    public IReadOnlyList<ClickPoint> Points
    {
        get
        {
            lock (_sync)
            {
                return _points.ToArray();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _points.Count;
            }
        }
    }

    /// <summary>Default dwell time stamped onto newly captured points.</summary>
    public int DefaultDelayMs { get; set; }

    /// <summary>Captures the current cursor position as a new point.</summary>
    public ClickPoint Record(string? label = null)
    {
        var position = _cursor.GetCursorPosition();
        return Record(position, label);
    }

    /// <summary>Adds an explicit coordinate as a new point.</summary>
    public ClickPoint Record(ScreenPoint position, string? label = null)
    {
        var point = new ClickPoint(position.X, position.Y, DefaultDelayMs, label);
        int index;

        lock (_sync)
        {
            _points.Add(point);
            index = _points.Count - 1;
        }

        PointRecorded?.Invoke(this, new PointRecordedEventArgs(point, index));
        PointsChanged?.Invoke(this, EventArgs.Empty);
        return point;
    }

    public bool RemoveAt(int index)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _points.Count)
            {
                return false;
            }

            _points.RemoveAt(index);
        }

        PointsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Drops the most recently captured point.</summary>
    public bool RemoveLast()
    {
        lock (_sync)
        {
            if (_points.Count == 0)
            {
                return false;
            }

            _points.RemoveAt(_points.Count - 1);
        }

        PointsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (_points.Count == 0)
            {
                return;
            }

            _points.Clear();
        }

        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the list, typically when a profile is loaded.</summary>
    public void Load(IEnumerable<ClickPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        lock (_sync)
        {
            _points.Clear();
            _points.AddRange(points.Select(p => p.Clone()));
        }

        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies the captured points into a profile's route.</summary>
    public void ApplyTo(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Target.Route = Points.Select(p => p.Clone()).ToList();
    }
}
