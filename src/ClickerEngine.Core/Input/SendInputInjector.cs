using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Native;

namespace ClickerEngine.Core.Input;

/// <summary>
/// The default backend: user-mode <c>SendInput</c>.
/// <para>
/// Keyboard events are sent as scan codes by default (<see cref="UseScanCodes"/>) because
/// games reading raw input or DirectInput often ignore virtual-key-only events. Mouse moves
/// are absolute over the whole virtual desktop.
/// </para>
/// <para>
/// Every event is tagged with <see cref="ExtraInfoSignature"/> in <c>dwExtraInfo</c> so the
/// hotkey hook can tell our own synthetic input apart from the user's real input.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SendInputInjector : IInputInjector
{
    /// <summary>Default value written into <c>dwExtraInfo</c> on every injected event.</summary>
    public const long DefaultExtraInfoSignature = 0x47_43_4C_4B; // "GCLK"

    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    private readonly IScreenGeometry _screen;

    public SendInputInjector()
        : this(new Win32ScreenGeometry())
    {
    }

    public SendInputInjector(IScreenGeometry screen)
    {
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
    }

    public string Name => "SendInput (user mode)";

    /// <summary>Send keyboard events as hardware scan codes. Better game compatibility.</summary>
    public bool UseScanCodes { get; set; } = true;

    /// <summary>
    /// Adds <c>MOUSEEVENTF_MOVE_NOCOALESCE</c> so rapid moves are not merged by the OS,
    /// which keeps smooth movement smooth.
    /// </summary>
    public bool UseNoCoalesce { get; set; } = true;

    /// <summary>Value written into <c>dwExtraInfo</c>; used to recognise our own events.</summary>
    public long ExtraInfoSignature { get; set; } = DefaultExtraInfoSignature;

    /// <summary>
    /// Throw when <c>SendInput</c> refuses an event. The usual cause is UIPI: a process at a
    /// lower integrity level cannot inject into an elevated window, so the clicker must be
    /// run as administrator. Failing loudly beats clicking into the void.
    /// </summary>
    public bool ThrowOnFailure { get; set; } = true;

    public ScreenPoint GetCursorPosition()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return ScreenPoint.Zero;
        }

        return new ScreenPoint(point.X, point.Y);
    }

    public void MoveTo(ScreenPoint position)
    {
        var bounds = _screen.VirtualBounds;
        var clamped = bounds.Clamp(position);
        var (nx, ny) = AbsoluteCoordinates.Normalize(clamped, bounds);

        uint flags = NativeMethods.MOUSEEVENTF_MOVE
            | NativeMethods.MOUSEEVENTF_ABSOLUTE
            | NativeMethods.MOUSEEVENTF_VIRTUALDESK;

        if (UseNoCoalesce)
        {
            flags |= NativeMethods.MOUSEEVENTF_MOVE_NOCOALESCE;
        }

        var input = CreateMouseInput(flags, mouseData: 0);
        input.u.mi.dx = nx;
        input.u.mi.dy = ny;
        Send(input);
    }

    public void MouseButtonDown(MouseButton button) => SendButton(button, down: true);

    public void MouseButtonUp(MouseButton button) => SendButton(button, down: false);

    public void KeyDown(VirtualKey key) => SendKey(key, down: true);

    public void KeyUp(VirtualKey key) => SendKey(key, down: false);

    private static bool IsExtendedKey(VirtualKey key) => key switch
    {
        VirtualKey.Insert or VirtualKey.Delete or VirtualKey.Home or VirtualKey.End or
        VirtualKey.PageUp or VirtualKey.PageDown or
        VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
        VirtualKey.NumLock or VirtualKey.PrintScreen or VirtualKey.Divide or
        VirtualKey.RightControl or VirtualKey.RightAlt => true,
        _ => false,
    };

    private void SendButton(MouseButton button, bool down)
    {
        uint flags;
        uint mouseData = 0;

        switch (button)
        {
            case MouseButton.Left:
                flags = down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP;
                break;
            case MouseButton.Right:
                flags = down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP;
                break;
            case MouseButton.Middle:
                flags = down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP;
                break;
            case MouseButton.XButton1:
                flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                mouseData = NativeMethods.XBUTTON1;
                break;
            case MouseButton.XButton2:
                flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                mouseData = NativeMethods.XBUTTON2;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(button), button, "Unknown mouse button.");
        }

        Send(CreateMouseInput(flags, mouseData));
    }

    private void SendKey(VirtualKey key, bool down)
    {
        if (key == VirtualKey.None)
        {
            return;
        }

        ushort vk = (ushort)key;
        ushort scan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);

        uint flags = down ? 0 : NativeMethods.KEYEVENTF_KEYUP;

        // Scan-code injection needs a scan code; media keys and a few others have none, so
        // fall back to the virtual key for those.
        bool useScan = UseScanCodes && scan != 0;
        if (useScan)
        {
            flags |= NativeMethods.KEYEVENTF_SCANCODE;
        }

        if (IsExtendedKey(key))
        {
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = useScan ? (ushort)0 : vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = new IntPtr(ExtraInfoSignature),
                },
            },
        };

        Send(input);
    }

    private NativeMethods.INPUT CreateMouseInput(uint flags, uint mouseData) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        u = new NativeMethods.InputUnion
        {
            mi = new NativeMethods.MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = mouseData,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = new IntPtr(ExtraInfoSignature),
            },
        },
    };

    private void Send(NativeMethods.INPUT input)
    {
        var buffer = new[] { input };
        uint sent = NativeMethods.SendInput(1, buffer, InputSize);
        if (sent == 1 || !ThrowOnFailure)
        {
            return;
        }

        int error = Marshal.GetLastWin32Error();
        throw new InputInjectionException(
            "SendInput was blocked by the system. The usual cause is User Interface Privilege " +
            "Isolation: run the clicker elevated when the target window is elevated.",
            new Win32Exception(error));
    }
}

/// <summary>Thrown when the OS refuses injected input.</summary>
public sealed class InputInjectionException : Exception
{
    public InputInjectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Virtual desktop bounds from <c>GetSystemMetrics</c>.</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32ScreenGeometry : IScreenGeometry
{
    public ScreenRect VirtualBounds
    {
        get
        {
            int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

            // A zero width means the metric was unavailable; fall back to the primary monitor.
            if (width <= 0 || height <= 0)
            {
                width = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN));
                height = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));
                left = 0;
                top = 0;
            }

            return new ScreenRect(left, top, width, height);
        }
    }
}
