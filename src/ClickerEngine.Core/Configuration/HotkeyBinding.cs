using System.Text;

namespace ClickerEngine.Core.Configuration;

/// <summary>
/// One hotkey: a key (or mouse button, since mouse buttons have virtual key codes) plus
/// optional Ctrl/Alt/Shift/Win modifiers.
/// </summary>
public sealed class HotkeyBinding : IEquatable<HotkeyBinding>
{
    public HotkeyBinding()
    {
    }

    public HotkeyBinding(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>The trigger key. <see cref="VirtualKey.None"/> means "not bound".</summary>
    public VirtualKey Key { get; set; } = VirtualKey.None;

    /// <summary>Modifiers that must be held for the binding to match.</summary>
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.None;

    /// <summary>False when nothing is bound, in which case the hotkey service ignores it.</summary>
    public bool IsBound => Key != VirtualKey.None;

    /// <summary>True when the binding is on a mouse button rather than a keyboard key.</summary>
    public bool IsMouseBinding => Key.IsMouseButton();

    /// <summary>Matches an observed key event against this binding.</summary>
    public bool Matches(VirtualKey key, KeyModifiers activeModifiers) =>
        IsBound && key == Key && activeModifiers == Modifiers;

    public HotkeyBinding Clone() => new(Key, Modifiers);

    public bool Equals(HotkeyBinding? other) =>
        other is not null && other.Key == Key && other.Modifiers == Modifiers;

    public override bool Equals(object? obj) => Equals(obj as HotkeyBinding);

    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);

    /// <summary>Human readable form such as <c>Ctrl+Shift+F6</c>.</summary>
    public override string ToString()
    {
        if (!IsBound)
        {
            return "(none)";
        }

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control))
        {
            sb.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Alt))
        {
            sb.Append("Alt+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Shift))
        {
            sb.Append("Shift+");
        }

        if (Modifiers.HasFlag(KeyModifiers.Windows))
        {
            sb.Append("Win+");
        }

        sb.Append(Key);
        return sb.ToString();
    }
}
