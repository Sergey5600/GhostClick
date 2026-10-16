using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Input;

/// <summary>
/// Forwards to the real injector or to a recorder, depending on a flag the user can flip at
/// runtime.
/// <para>
/// This is what makes a front end safe to poke at: with dry run on you can watch the exact
/// input stream a profile produces without the mouse actually running away across the
/// desktop.
/// </para>
/// </summary>
public sealed class SwitchableInputInjector : IInputInjector
{
    private readonly IInputInjector _live;
    private readonly RecordingInputInjector _dryRun;

    public SwitchableInputInjector(IInputInjector live, RecordingInputInjector dryRun)
    {
        _live = live ?? throw new ArgumentNullException(nameof(live));
        _dryRun = dryRun ?? throw new ArgumentNullException(nameof(dryRun));
    }

    private bool _seeded;

    /// <summary>When true, nothing reaches the operating system.</summary>
    public bool DryRun { get; set; }

    /// <summary>The recorder used while <see cref="DryRun"/> is on.</summary>
    public RecordingInputInjector Recorder => _dryRun;

    public string Name => DryRun ? _dryRun.Name : _live.Name;

    private IInputInjector Active => DryRun ? _dryRun : _live;

    /// <summary>
    /// In dry run the plan must see the cursor it thinks it has moved, or an interpolated
    /// move would replan from the real position on every step and never appear to travel.
    /// The simulated cursor is seeded once from the real one, then follows the plan.
    /// </summary>
    public ScreenPoint GetCursorPosition()
    {
        if (!DryRun)
        {
            return _live.GetCursorPosition();
        }

        if (!_seeded)
        {
            _dryRun.SetCursorPosition(_live.GetCursorPosition());
            _seeded = true;
        }

        return _dryRun.GetCursorPosition();
    }

    /// <summary>
    /// Re-syncs the simulated cursor with the real one. Front ends call this when a run
    /// starts, so each dry run begins from where the mouse actually is.
    /// </summary>
    public void ResetDryRunCursor() => _seeded = false;

    public void MoveTo(ScreenPoint position) => Active.MoveTo(position);

    public void MouseButtonDown(MouseButton button) => Active.MouseButtonDown(button);

    public void MouseButtonUp(MouseButton button) => Active.MouseButtonUp(button);

    public void KeyDown(VirtualKey key) => Active.KeyDown(key);

    public void KeyUp(VirtualKey key) => Active.KeyUp(key);
}

/// <summary>
/// Passes window queries through to a real probe only while input is real. In dry run nothing
/// reaches any window, so there is nothing to hold for: every answer is "unknown".
/// </summary>
public sealed class DryRunAwareWindowProbe : IWindowProbe
{
    private readonly IWindowProbe _inner;
    private readonly SwitchableInputInjector _injector;

    public DryRunAwareWindowProbe(IWindowProbe inner, SwitchableInputInjector injector)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
    }

    public long GetForegroundWindow() => _injector.DryRun ? 0 : _inner.GetForegroundWindow();

    public long GetWindowAt(ScreenPoint point) => _injector.DryRun ? 0 : _inner.GetWindowAt(point);

    public bool IsOwnWindow(long window) => !_injector.DryRun && _inner.IsOwnWindow(window);
}
