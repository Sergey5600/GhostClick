using System.Text;
using System.Text.Json.Serialization;

namespace ClickerEngine.Core.Configuration;

/// <summary>
/// One thing the engine presses on an action tick: a keyboard key or a mouse button, plus the
/// Ctrl/Alt/Shift/Win modifiers held around it. Mouse buttons are expressed through their
/// virtual key codes, so a single list can mix both.
/// </summary>
public sealed class InputChord : IEquatable<InputChord>
{
    public InputChord()
    {
    }

    public InputChord(VirtualKey key, KeyModifiers modifiers = KeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>The key or mouse button to press.</summary>
    public VirtualKey Key { get; set; } = VirtualKey.None;

    /// <summary>Modifiers held down around the press.</summary>
    public KeyModifiers Modifiers { get; set; } = KeyModifiers.None;

    /// <summary>True when <see cref="Key"/> is a mouse button.</summary>
    [JsonIgnore]
    public bool IsMouse => Key.IsMouseButton();

    /// <summary>A chord that clicks a mouse button.</summary>
    public static InputChord Mouse(MouseButton button, KeyModifiers modifiers = KeyModifiers.None) =>
        new(button.ToVirtualKey(), modifiers);

    public InputChord Clone() => new(Key, Modifiers);

    public bool Equals(InputChord? other) =>
        other is not null && other.Key == Key && other.Modifiers == Modifiers;

    public override bool Equals(object? obj) => Equals(obj as InputChord);

    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);

    /// <summary>Human readable form such as <c>Ctrl+E</c> or <c>LeftButton</c>.</summary>
    public override string ToString()
    {
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
