using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Points;

namespace ClickerEngine.Core.Hotkeys;

/// <summary>
/// Wires an <see cref="IHotkeyService"/> to an <see cref="AutoClickerEngine"/>.
/// <para>
/// This is where Toggle and Hold mode actually differ: in Toggle mode a press flips the run
/// on or off, in Hold mode the press starts it and the release stops it. Keeping that in one
/// small class means neither the engine nor the hotkey plumbing has to know about the other.
/// </para>
/// </summary>
public sealed class HotkeyController : IDisposable
{
    private readonly IHotkeyService _hotkeys;
    private readonly AutoClickerEngine _engine;
    private readonly PointRecorder? _recorder;
    private bool _disposed;

    public HotkeyController(IHotkeyService hotkeys, AutoClickerEngine engine, PointRecorder? recorder = null)
    {
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _recorder = recorder;

        _hotkeys.HotkeyTriggered += OnHotkeyTriggered;
    }

    /// <summary>Raised when the record hotkey captures a point.</summary>
    public event EventHandler<PointRecordedEventArgs>? PointRecorded;

    /// <summary>Pushes the engine profile's hotkeys into the service and starts listening.</summary>
    public void Start()
    {
        _hotkeys.Configure(_engine.Profile.Hotkeys);
        _hotkeys.Start();
    }

    /// <summary>Re-reads the bindings from the engine's current profile.</summary>
    public void Reconfigure() => _hotkeys.Configure(_engine.Profile.Hotkeys);

    public void Stop() => _hotkeys.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeys.HotkeyTriggered -= OnHotkeyTriggered;
    }

    private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
    {
        switch (e.Action)
        {
            case HotkeyAction.EmergencyStop:
                if (e.IsPressed && _engine.Profile.Safety.FailsafeEnabled)
                {
                    _engine.EmergencyStop();
                }

                break;

            case HotkeyAction.StartStop:
                HandleStartStop(e.IsPressed);
                break;

            case HotkeyAction.Pause:
                if (e.IsPressed)
                {
                    _engine.TogglePause();
                }

                break;

            case HotkeyAction.RecordPoint:
                if (e.IsPressed && _recorder is not null)
                {
                    var point = _recorder.Record();
                    PointRecorded?.Invoke(this, new PointRecordedEventArgs(point, _recorder.Count - 1));
                }

                break;
        }
    }

    private void HandleStartStop(bool isPressed)
    {
        if (_engine.Profile.Mode == ClickMode.Hold)
        {
            if (isPressed)
            {
                _engine.Start();
            }
            else
            {
                _engine.Stop(StopReason.HoldReleased);
            }

            return;
        }

        // Toggle mode acts on the press and ignores the release.
        if (isPressed)
        {
            _engine.Toggle();
        }
    }
}
