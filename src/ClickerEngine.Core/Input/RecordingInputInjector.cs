using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Input;

/// <summary>The kind of an event captured by <see cref="RecordingInputInjector"/>.</summary>
public enum InjectedEventKind
{
    Move,
    MouseDown,
    MouseUp,
    KeyDown,
    KeyUp,
}

/// <summary>One captured input event.</summary>
public readonly record struct InjectedEvent(
    InjectedEventKind Kind,
    ScreenPoint Position,
    MouseButton Button,
    VirtualKey Key)
{
    public static InjectedEvent Move(ScreenPoint position) =>
        new(InjectedEventKind.Move, position, default, default);

    public static InjectedEvent MouseDown(MouseButton button, ScreenPoint position) =>
        new(InjectedEventKind.MouseDown, position, button, default);

    public static InjectedEvent MouseUp(MouseButton button, ScreenPoint position) =>
        new(InjectedEventKind.MouseUp, position, button, default);

    public static InjectedEvent KeyDown(VirtualKey key) =>
        new(InjectedEventKind.KeyDown, default, default, key);

    public static InjectedEvent KeyUp(VirtualKey key) =>
        new(InjectedEventKind.KeyUp, default, default, key);

    public override string ToString() => Kind switch
    {
        InjectedEventKind.Move => $"Move {Position}",
        InjectedEventKind.MouseDown => $"MouseDown {Button} @ {Position}",
        InjectedEventKind.MouseUp => $"MouseUp {Button} @ {Position}",
        InjectedEventKind.KeyDown => $"KeyDown {Key}",
        _ => $"KeyUp {Key}",
    };
}

/// <summary>
/// An injector that records what it was asked to do instead of touching the OS. Used by the
/// unit tests and by the dev UI's dry-run mode, where you want to watch the generated input
/// stream without actually moving the mouse.
/// </summary>
public sealed class RecordingInputInjector : IInputInjector
{
    private readonly List<InjectedEvent> _events = new();
    private readonly object _sync = new();
    private ScreenPoint _cursor;

    public RecordingInputInjector()
        : this(ScreenPoint.Zero)
    {
    }

    public RecordingInputInjector(ScreenPoint initialCursor)
    {
        _cursor = initialCursor;
    }

    public string Name => "Recording (dry run)";

    /// <summary>Everything recorded so far, oldest first.</summary>
    public IReadOnlyList<InjectedEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }
    }

    /// <summary>Raised for every recorded event, so a UI can stream it into a log.</summary>
    public event EventHandler<InjectedEvent>? EventRecorded;

    public ScreenPoint GetCursorPosition()
    {
        lock (_sync)
        {
            return _cursor;
        }
    }

    /// <summary>Moves the simulated cursor without recording a Move event.</summary>
    public void SetCursorPosition(ScreenPoint position)
    {
        lock (_sync)
        {
            _cursor = position;
        }
    }

    public void MoveTo(ScreenPoint position)
    {
        lock (_sync)
        {
            _cursor = position;
        }

        Record(InjectedEvent.Move(position));
    }

    public void MouseButtonDown(MouseButton button) => Record(InjectedEvent.MouseDown(button, GetCursorPosition()));

    public void MouseButtonUp(MouseButton button) => Record(InjectedEvent.MouseUp(button, GetCursorPosition()));

    public void KeyDown(VirtualKey key) => Record(InjectedEvent.KeyDown(key));

    public void KeyUp(VirtualKey key) => Record(InjectedEvent.KeyUp(key));

    public void Clear()
    {
        lock (_sync)
        {
            _events.Clear();
        }
    }

    private void Record(InjectedEvent injected)
    {
        lock (_sync)
        {
            _events.Add(injected);
        }

        EventRecorded?.Invoke(this, injected);
    }
}

/// <summary>An injector that swallows everything. Useful as a safe default.</summary>
public sealed class NullInputInjector : IInputInjector
{
    public static readonly NullInputInjector Instance = new();

    public string Name => "None";

    public ScreenPoint GetCursorPosition() => ScreenPoint.Zero;

    public void MoveTo(ScreenPoint position)
    {
    }

    public void MouseButtonDown(MouseButton button)
    {
    }

    public void MouseButtonUp(MouseButton button)
    {
    }

    public void KeyDown(VirtualKey key)
    {
    }

    public void KeyUp(VirtualKey key)
    {
    }
}

/// <summary>A fixed screen rectangle; the tests and the dry-run mode use it.</summary>
public sealed class FixedScreenGeometry : IScreenGeometry
{
    public FixedScreenGeometry(ScreenRect bounds) => VirtualBounds = bounds;

    public FixedScreenGeometry(int width, int height)
        : this(new ScreenRect(0, 0, width, height))
    {
    }

    public ScreenRect VirtualBounds { get; }
}
