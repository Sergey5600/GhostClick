using System.Globalization;
using ClickerEngine.Core.Configuration;

namespace GhostClick.Bridge;

/// <summary>
/// Translates between the key names the web interface uses and the engine's
/// <see cref="VirtualKey"/>.
/// <para>
/// The page names keyboard keys by <c>KeyboardEvent.code</c> (<c>KeyQ</c>, <c>Digit1</c>,
/// <c>F6</c>, <c>ControlLeft</c>), which identifies the physical key whatever the layout, and
/// mouse buttons as <c>Mouse1</c>..<c>Mouse5</c> (4 = X1, 5 = X2). A virtual key with no web
/// name round-trips as <c>VK_xx</c>, so a profile edited elsewhere never loses a binding.
/// </para>
/// </summary>
public static class KeyCodes
{
    private const string RawPrefix = "VK_";

    private static readonly Dictionary<string, VirtualKey> ByCode = new(StringComparer.Ordinal);
    private static readonly Dictionary<VirtualKey, string> ByKey = new();
    private static readonly Dictionary<VirtualKey, string> Labels = new();

    static KeyCodes()
    {
        Add("Mouse1", VirtualKey.LeftButton, "ЛКМ");
        Add("Mouse2", VirtualKey.RightButton, "ПКМ");
        Add("Mouse3", VirtualKey.MiddleButton, "СКМ");
        Add("Mouse4", VirtualKey.XButton1, "X1");
        Add("Mouse5", VirtualKey.XButton2, "X2");

        Add("Escape", VirtualKey.Escape, "Esc");
        for (int i = 1; i <= 24; i++)
        {
            Add("F" + i, VirtualKey.F1 + (i - 1), "F" + i);
        }

        for (int i = 0; i <= 9; i++)
        {
            Add("Digit" + i, VirtualKey.D0 + i, i.ToString(CultureInfo.InvariantCulture));
            Add("Numpad" + i, VirtualKey.NumPad0 + i, "Num " + i);
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            Add("Key" + c, VirtualKey.A + (c - 'A'), c.ToString());
        }

        Add("Backquote", VirtualKey.Oem3, "`");
        Add("Minus", VirtualKey.OemMinus, "-");
        Add("Equal", VirtualKey.OemPlus, "=");
        Add("Backspace", VirtualKey.Back, "Backspace");
        Add("Tab", VirtualKey.Tab, "Tab");
        Add("BracketLeft", VirtualKey.Oem4, "[");
        Add("BracketRight", VirtualKey.Oem6, "]");
        Add("Backslash", VirtualKey.Oem5, "\\");
        Add("CapsLock", VirtualKey.CapsLock, "Caps");
        Add("Semicolon", VirtualKey.Oem1, ";");
        Add("Quote", VirtualKey.Oem7, "'");
        Add("Enter", VirtualKey.Enter, "Enter");
        Add("ShiftLeft", VirtualKey.LeftShift, "Shift");
        Add("ShiftRight", VirtualKey.RightShift, "Shift");
        Add("Comma", VirtualKey.OemComma, ",");
        Add("Period", VirtualKey.OemPeriod, ".");
        Add("Slash", VirtualKey.Oem2, "/");
        Add("IntlBackslash", VirtualKey.Oem102, "\\");
        Add("ControlLeft", VirtualKey.LeftControl, "Ctrl");
        Add("ControlRight", VirtualKey.RightControl, "Ctrl");
        Add("MetaLeft", VirtualKey.LeftWindows, "Win");
        Add("MetaRight", VirtualKey.RightWindows, "Win");
        Add("AltLeft", VirtualKey.LeftAlt, "Alt");
        Add("AltRight", VirtualKey.RightAlt, "Alt");
        Add("Space", VirtualKey.Space, "Space");
        Add("ContextMenu", VirtualKey.Apps, "Menu");
        Add("PrintScreen", VirtualKey.PrintScreen, "PrtSc");
        Add("ScrollLock", VirtualKey.ScrollLock, "ScrLk");
        Add("Pause", VirtualKey.Pause, "Pause");
        Add("Insert", VirtualKey.Insert, "Ins");
        Add("Home", VirtualKey.Home, "Home");
        Add("PageUp", VirtualKey.PageUp, "PgUp");
        Add("Delete", VirtualKey.Delete, "Del");
        Add("End", VirtualKey.End, "End");
        Add("PageDown", VirtualKey.PageDown, "PgDn");
        Add("ArrowUp", VirtualKey.Up, "↑");
        Add("ArrowDown", VirtualKey.Down, "↓");
        Add("ArrowLeft", VirtualKey.Left, "←");
        Add("ArrowRight", VirtualKey.Right, "→");
        Add("NumLock", VirtualKey.NumLock, "NumLk");
        Add("NumpadMultiply", VirtualKey.Multiply, "Num *");
        Add("NumpadAdd", VirtualKey.Add, "Num +");
        Add("NumpadSubtract", VirtualKey.Subtract, "Num -");
        Add("NumpadDecimal", VirtualKey.Decimal, "Num .");
        Add("NumpadDivide", VirtualKey.Divide, "Num /");

        // One-way aliases: side-less modifiers come out as the left key, and the numpad
        // Enter is the same virtual key as the main one.
        ByKey[VirtualKey.Shift] = "ShiftLeft";
        ByKey[VirtualKey.Control] = "ControlLeft";
        ByKey[VirtualKey.Alt] = "AltLeft";
        Labels[VirtualKey.Shift] = "Shift";
        Labels[VirtualKey.Control] = "Ctrl";
        Labels[VirtualKey.Alt] = "Alt";
        ByCode["NumpadEnter"] = VirtualKey.Enter;
    }

    /// <summary>Parses a web key name. Accepts every name <see cref="ToCode"/> produces.</summary>
    public static bool TryGetVirtualKey(string? code, out VirtualKey key)
    {
        key = VirtualKey.None;
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        if (ByCode.TryGetValue(code, out key))
        {
            return true;
        }

        if (code.StartsWith(RawPrefix, StringComparison.Ordinal) &&
            int.TryParse(code.AsSpan(RawPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int raw) &&
            raw is > 0 and <= 0xFE)
        {
            key = (VirtualKey)raw;
            return true;
        }

        return false;
    }

    /// <summary>The web name for a key; <c>VK_xx</c> for keys the page has no name for.</summary>
    public static string ToCode(VirtualKey key) =>
        ByKey.TryGetValue(key, out var code)
            ? code
            : RawPrefix + ((int)key).ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>The short label the page prints on the key, e.g. <c>ЛКМ</c>, <c>F6</c>, <c>PgUp</c>.</summary>
    public static string Label(VirtualKey key) =>
        Labels.TryGetValue(key, out var label) ? label : ToCode(key);

    /// <summary>Parses modifier names (<c>Ctrl</c>, <c>Shift</c>, <c>Alt</c>, <c>Win</c>); unknown names are ignored.</summary>
    public static KeyModifiers ParseModifiers(IEnumerable<string>? names)
    {
        var modifiers = KeyModifiers.None;
        foreach (var name in names ?? Array.Empty<string>())
        {
            modifiers |= name switch
            {
                "Ctrl" => KeyModifiers.Control,
                "Shift" => KeyModifiers.Shift,
                "Alt" => KeyModifiers.Alt,
                "Win" => KeyModifiers.Windows,
                _ => KeyModifiers.None,
            };
        }

        return modifiers;
    }

    /// <summary>Modifier names in the order the page shows them: Ctrl, Shift, Alt, Win.</summary>
    public static string[] FormatModifiers(KeyModifiers modifiers)
    {
        var names = new List<string>(4);
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            names.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            names.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            names.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Windows))
        {
            names.Add("Win");
        }

        return names.ToArray();
    }

    /// <summary>A chord as the page writes it, e.g. <c>Ctrl + F12</c>.</summary>
    public static string Describe(VirtualKey key, KeyModifiers modifiers) =>
        string.Join(" + ", FormatModifiers(modifiers).Append(Label(key)));

    private static void Add(string code, VirtualKey key, string label)
    {
        ByCode[code] = key;
        ByKey[key] = code;
        Labels[key] = label;
    }
}
