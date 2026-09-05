using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Input;

/// <summary>Reads the current mouse position.</summary>
public interface ICursorProvider
{
    /// <summary>Current cursor position in virtual-desktop pixels.</summary>
    ScreenPoint GetCursorPosition();
}

/// <summary>
/// The single seam between the engine and the operating system's input stack.
/// <para>
/// Everything the engine does is expressed as the five primitives below, so an alternative
/// backend (a signed low-level driver, a hardware device, a remote agent) can be dropped in
/// by implementing this interface - no other part of the engine changes.
/// </para>
/// </summary>
public interface IInputInjector : ICursorProvider
{
    /// <summary>A short name for the backend, shown in the UI.</summary>
    string Name { get; }

    /// <summary>Moves the cursor to an absolute virtual-desktop position.</summary>
    void MoveTo(ScreenPoint position);

    /// <summary>Presses a mouse button and leaves it down.</summary>
    void MouseButtonDown(MouseButton button);

    /// <summary>Releases a previously pressed mouse button.</summary>
    void MouseButtonUp(MouseButton button);

    /// <summary>Presses a key and leaves it down.</summary>
    void KeyDown(VirtualKey key);

    /// <summary>Releases a previously pressed key.</summary>
    void KeyUp(VirtualKey key);
}

/// <summary>Reports the bounds the cursor may be moved within.</summary>
public interface IScreenGeometry
{
    /// <summary>The union of all monitors, in virtual-desktop pixels.</summary>
    ScreenRect VirtualBounds { get; }
}
