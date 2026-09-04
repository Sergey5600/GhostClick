namespace ClickerEngine.Core.Configuration;

/// <summary>
/// Win32 virtual key codes. The low codes (0x01-0x06) are the mouse buttons, which is why a
/// single enum can describe both keyboard and mouse hotkeys.
/// </summary>
public enum VirtualKey
{
    None = 0x00,

    // Mouse buttons - usable as hotkeys.
    LeftButton = 0x01,
    RightButton = 0x02,
    Cancel = 0x03,
    MiddleButton = 0x04,
    XButton1 = 0x05,
    XButton2 = 0x06,

    Back = 0x08,
    Tab = 0x09,
    Clear = 0x0C,
    Enter = 0x0D,
    Shift = 0x10,
    Control = 0x11,
    Alt = 0x12,
    Pause = 0x13,
    CapsLock = 0x14,
    Escape = 0x1B,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Select = 0x29,
    Print = 0x2A,
    Execute = 0x2B,
    PrintScreen = 0x2C,
    Insert = 0x2D,
    Delete = 0x2E,
    Help = 0x2F,

    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,

    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,

    LeftWindows = 0x5B,
    RightWindows = 0x5C,
    Apps = 0x5D,
    Sleep = 0x5F,

    NumPad0 = 0x60,
    NumPad1 = 0x61,
    NumPad2 = 0x62,
    NumPad3 = 0x63,
    NumPad4 = 0x64,
    NumPad5 = 0x65,
    NumPad6 = 0x66,
    NumPad7 = 0x67,
    NumPad8 = 0x68,
    NumPad9 = 0x69,
    Multiply = 0x6A,
    Add = 0x6B,
    Separator = 0x6C,
    Subtract = 0x6D,
    Decimal = 0x6E,
    Divide = 0x6F,

    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,
    F13 = 0x7C,
    F14 = 0x7D,
    F15 = 0x7E,
    F16 = 0x7F,
    F17 = 0x80,
    F18 = 0x81,
    F19 = 0x82,
    F20 = 0x83,
    F21 = 0x84,
    F22 = 0x85,
    F23 = 0x86,
    F24 = 0x87,

    NumLock = 0x90,
    ScrollLock = 0x91,

    LeftShift = 0xA0,
    RightShift = 0xA1,
    LeftControl = 0xA2,
    RightControl = 0xA3,
    LeftAlt = 0xA4,
    RightAlt = 0xA5,

    BrowserBack = 0xA6,
    BrowserForward = 0xA7,
    VolumeMute = 0xAD,
    VolumeDown = 0xAE,
    VolumeUp = 0xAF,
    MediaNextTrack = 0xB0,
    MediaPrevTrack = 0xB1,
    MediaStop = 0xB2,
    MediaPlayPause = 0xB3,

    Oem1 = 0xBA,        // ;:
    OemPlus = 0xBB,     // =+
    OemComma = 0xBC,    // ,<
    OemMinus = 0xBD,    // -_
    OemPeriod = 0xBE,   // .>
    Oem2 = 0xBF,        // /?
    Oem3 = 0xC0,        // `~
    Oem4 = 0xDB,        // [{
    Oem5 = 0xDC,        // \|
    Oem6 = 0xDD,        // ]}
    Oem7 = 0xDE,        // '"
    Oem102 = 0xE2,      // <> on ISO keyboards
}

/// <summary>Helpers for classifying <see cref="VirtualKey"/> values.</summary>
public static class VirtualKeyExtensions
{
    /// <summary>True when the key code actually denotes a mouse button.</summary>
    public static bool IsMouseButton(this VirtualKey key) => key is
        VirtualKey.LeftButton or VirtualKey.RightButton or VirtualKey.MiddleButton or
        VirtualKey.XButton1 or VirtualKey.XButton2;

    /// <summary>True for Shift/Control/Alt/Windows in any of their variants.</summary>
    public static bool IsModifier(this VirtualKey key) => key is
        VirtualKey.Shift or VirtualKey.Control or VirtualKey.Alt or
        VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.LeftAlt or VirtualKey.RightAlt or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    /// <summary>
    /// Maps a mouse-button key code onto the <see cref="MouseButton"/> used by the injector.
    /// Returns <see langword="false"/> for keyboard keys.
    /// </summary>
    public static bool TryGetMouseButton(this VirtualKey key, out MouseButton button)
    {
        switch (key)
        {
            case VirtualKey.LeftButton: button = MouseButton.Left; return true;
            case VirtualKey.RightButton: button = MouseButton.Right; return true;
            case VirtualKey.MiddleButton: button = MouseButton.Middle; return true;
            case VirtualKey.XButton1: button = MouseButton.XButton1; return true;
            case VirtualKey.XButton2: button = MouseButton.XButton2; return true;
            default: button = MouseButton.Left; return false;
        }
    }

    /// <summary>Inverse of <see cref="TryGetMouseButton"/>.</summary>
    public static VirtualKey ToVirtualKey(this MouseButton button) => button switch
    {
        MouseButton.Left => VirtualKey.LeftButton,
        MouseButton.Right => VirtualKey.RightButton,
        MouseButton.Middle => VirtualKey.MiddleButton,
        MouseButton.XButton1 => VirtualKey.XButton1,
        MouseButton.XButton2 => VirtualKey.XButton2,
        _ => VirtualKey.LeftButton,
    };

    /// <summary>
    /// The modifier flag a key contributes when held, or <see cref="KeyModifiers.None"/>
    /// for ordinary keys.
    /// </summary>
    public static KeyModifiers ToModifierFlag(this VirtualKey key) => key switch
    {
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift => KeyModifiers.Shift,
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl => KeyModifiers.Control,
        VirtualKey.Alt or VirtualKey.LeftAlt or VirtualKey.RightAlt => KeyModifiers.Alt,
        VirtualKey.LeftWindows or VirtualKey.RightWindows => KeyModifiers.Windows,
        _ => KeyModifiers.None,
    };

    /// <summary>The concrete key sent to hold down a modifier flag.</summary>
    public static VirtualKey ToKey(this KeyModifiers modifier) => modifier switch
    {
        KeyModifiers.Alt => VirtualKey.LeftAlt,
        KeyModifiers.Control => VirtualKey.LeftControl,
        KeyModifiers.Shift => VirtualKey.LeftShift,
        KeyModifiers.Windows => VirtualKey.LeftWindows,
        _ => VirtualKey.None,
    };
}
