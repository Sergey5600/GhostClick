using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Hotkeys;

/// <summary>The roles a binding can play.</summary>
public enum HotkeyAction
{
    /// <summary>Toggle a run, or in Hold mode the key that must stay pressed.</summary>
    StartStop,

    /// <summary>Pause / resume.</summary>
    Pause,

    /// <summary>Stop right now.</summary>
    EmergencyStop,

    /// <summary>Capture the cursor position.</summary>
    RecordPoint,
}

/// <summary>Reports a hotkey press or release.</summary>
public sealed class HotkeyEventArgs : EventArgs
{
    public HotkeyEventArgs(HotkeyAction action, HotkeyBinding binding, bool isPressed)
    {
        Action = action;
        Binding = binding;
        IsPressed = isPressed;
    }

    public HotkeyAction Action { get; }

    public HotkeyBinding Binding { get; }

    /// <summary>True for key-down, false for key-up.</summary>
    public bool IsPressed { get; }
}

/// <summary>
/// Watches for global hotkeys - global meaning they fire whether or not the app has focus.
/// <para>
/// Implementations raise <see cref="HotkeyTriggered"/> off the OS callback thread, so
/// handlers may block without stalling the system's input queue.
/// </para>
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>Raised on every press and release of a registered binding.</summary>
    event EventHandler<HotkeyEventArgs>? HotkeyTriggered;

    /// <summary>True once the service is listening.</summary>
    bool IsRunning { get; }

    /// <summary>Replaces the whole binding set. Safe to call while running.</summary>
    void Configure(HotkeySettings settings);

    /// <summary>Begins listening.</summary>
    void Start();

    /// <summary>Stops listening.</summary>
    void Stop();
}

/// <summary>
/// A hotkey service that fires only when told to. The dev UI uses it for its on-screen
/// buttons and the tests use it to drive the engine deterministically.
/// </summary>
public sealed class ManualHotkeyService : IHotkeyService
{
    private HotkeySettings _settings = new();

    public event EventHandler<HotkeyEventArgs>? HotkeyTriggered;

    public bool IsRunning { get; private set; }

    public HotkeySettings Settings => _settings;

    public void Configure(HotkeySettings settings) =>
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public void Start() => IsRunning = true;

    public void Stop() => IsRunning = false;

    /// <summary>Raises a press followed immediately by a release.</summary>
    public void Trigger(HotkeyAction action)
    {
        Trigger(action, isPressed: true);
        Trigger(action, isPressed: false);
    }

    /// <summary>Raises a single press or release.</summary>
    public void Trigger(HotkeyAction action, bool isPressed) =>
        HotkeyTriggered?.Invoke(this, new HotkeyEventArgs(action, BindingFor(action), isPressed));

    public void Dispose() => Stop();

    private HotkeyBinding BindingFor(HotkeyAction action) => action switch
    {
        HotkeyAction.StartStop => _settings.StartStop,
        HotkeyAction.Pause => _settings.Pause,
        HotkeyAction.EmergencyStop => _settings.EmergencyStop,
        HotkeyAction.RecordPoint => _settings.RecordPoint,
        _ => new HotkeyBinding(),
    };
}
