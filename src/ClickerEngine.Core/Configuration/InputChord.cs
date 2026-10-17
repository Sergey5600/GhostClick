using System.Text;
using System.Text.Json.Serialization;

namespace ClickerEngine.Core.Configuration;

/// <summary>
/// One thing the engine presses: a keyboard key or a mouse button, plus the Ctrl/Alt/Shift/Win
/// modifiers held around it, and how it is pressed. Mouse buttons are expressed through their
/// virtual key codes, so a single list can mix both.
/// </summary>
public sealed class InputChord : IEquatable<InputChord>
{
    /// <summary>Press length of a click when nothing else is said, in milliseconds.</summary>
    public const int DefaultPressMs = 30;

    /// <summary>Hold length of a <see cref="HoldMode.Timed"/> hold when nothing else is said, in milliseconds.</summary>
    public const int DefaultHoldMs = 500;

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

    /// <summary>Clicked on every action tick, or held down.</summary>
    public PressMode Mode { get; set; } = PressMode.Click;

    /// <summary>
    /// How long each click holds the input down, in milliseconds. With
    /// <see cref="ActionSettings.RandomizeHoldDuration"/> every click varies around it by
    /// <see cref="ActionSettings.PressSpreadPercent"/>.
    /// </summary>
    public int PressMs { get; set; } = DefaultPressMs;

    /// <summary>For <see cref="PressMode.Hold"/>: down for the whole run, or for <see cref="HoldMs"/> on every tick.</summary>
    public HoldMode Hold { get; set; } = HoldMode.WhileRunning;

    /// <summary>For a <see cref="HoldMode.Timed"/> hold: how long it stays down on every tick, in milliseconds.</summary>
    public int HoldMs { get; set; } = DefaultHoldMs;

    /// <summary>True when <see cref="Key"/> is a mouse button.</summary>
    [JsonIgnore]
    public bool IsMouse => Key.IsMouseButton();

    /// <summary>
    /// True when the input is held for the whole run rather than pressed on the action ticks:
    /// the engine presses it when the run starts and releases it when the run pauses or ends.
    /// </summary>
    [JsonIgnore]
    public bool IsHeldWhileRunning => Mode == PressMode.Hold && Hold == HoldMode.WhileRunning;

    /// <summary>A chord that clicks a mouse button.</summary>
    public static InputChord Mouse(MouseButton button, KeyModifiers modifiers = KeyModifiers.None) =>
        new(button.ToVirtualKey(), modifiers);

    public InputChord Clone() => (InputChord)MemberwiseClone();

    public bool Equals(InputChord? other) =>
        other is not null &&
        other.Key == Key &&
        other.Modifiers == Modifiers &&
        other.Mode == Mode &&
        other.PressMs == PressMs &&
        other.Hold == Hold &&
        other.HoldMs == HoldMs;

    public override bool Equals(object? obj) => Equals(obj as InputChord);

    public override int GetHashCode() => HashCode.Combine(Key, Modifiers, Mode, PressMs, Hold, HoldMs);

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
