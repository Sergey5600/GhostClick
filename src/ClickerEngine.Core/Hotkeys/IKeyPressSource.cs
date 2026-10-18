using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Hotkeys;

/// <summary>Reports one press of a key or mouse button by the user, with the modifiers held at the time.</summary>
public sealed class KeyPressedEventArgs : EventArgs
{
    public KeyPressedEventArgs(VirtualKey key, KeyModifiers modifiers)
    {
        Key = key;
        Modifiers = modifiers;
    }

    public VirtualKey Key { get; }

    public KeyModifiers Modifiers { get; }

    /// <summary>
    /// True when this press is <paramref name="chord"/>: its key, with at least its modifiers
    /// held. Extra modifiers do not spoil it, so Shift+Space still answers a wait for Space.
    /// </summary>
    public bool Matches(InputChord chord) =>
        chord is not null && chord.Key == Key && (Modifiers & chord.Modifiers) == chord.Modifiers;
}

/// <summary>
/// Every key the user presses, bound to a hotkey or not - what a script's «Ждать клавишу»
/// listens to. Input the clicker injects itself is never reported.
/// <para>
/// Raised on the input thread: handlers must return at once.
/// </para>
/// </summary>
public interface IKeyPressSource
{
    event EventHandler<KeyPressedEventArgs>? KeyPressed;
}
