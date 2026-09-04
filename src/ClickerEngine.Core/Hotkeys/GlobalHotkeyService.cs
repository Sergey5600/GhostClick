using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Native;

namespace ClickerEngine.Core.Hotkeys;

/// <summary>
/// Global hotkeys via low-level <c>WH_KEYBOARD_LL</c> and <c>WH_MOUSE_LL</c> hooks.
/// <para>
/// Low-level hooks rather than <c>RegisterHotKey</c>, because the spec needs three things
/// <c>RegisterHotKey</c> cannot do: bind the extra mouse buttons (X1/X2), see key releases
/// (which Hold mode depends on), and bind bare keys that another app may also use.
/// </para>
/// <para>
/// Windows requires that low-level hooks be installed on a thread that pumps messages, so the
/// service owns a private thread with its own message loop. The hook callback itself only
/// matches and enqueues - the events are raised on a second thread, so a handler that blocks
/// (stopping the engine, for instance) can never stall the system input queue or get the hook
/// silently uninstalled for exceeding <c>LowLevelHooksTimeout</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkeyService : IHotkeyService
{
    private readonly object _sync = new();
    private readonly BlockingCollection<HotkeyEventArgs> _queue = new(new ConcurrentQueue<HotkeyEventArgs>());
    private readonly HashSet<HotkeyAction> _pressed = new();

    // The delegates must stay alive for as long as the hooks are installed; if they were
    // collected the OS would call into freed memory.
    private NativeMethods.HookProc? _keyboardProc;
    private NativeMethods.HookProc? _mouseProc;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private Thread? _hookThread;
    private Thread? _dispatchThread;
    private volatile uint _hookThreadId;
    private HotkeySettings _settings = new();
    private volatile bool _running;
    private bool _disposed;

    public event EventHandler<HotkeyEventArgs>? HotkeyTriggered;

    public bool IsRunning => _running;

    /// <summary>
    /// Swallow a matched hotkey so it never reaches the focused application. Off by default:
    /// a hotkey bound to a mouse button or a common game key would otherwise stop working in
    /// the game itself.
    /// </summary>
    public bool SuppressHotkeys { get; set; }

    /// <summary>
    /// Ignore events tagged with this value in <c>dwExtraInfo</c>. Set to the injector's
    /// signature so the clicker's own X-button clicks cannot retrigger its own hotkeys.
    /// </summary>
    public long IgnoredExtraInfo { get; set; } = SendInputInjector.DefaultExtraInfoSignature;

    public void Configure(HotkeySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_sync)
        {
            _settings = settings.Clone();
            _pressed.Clear();
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_running)
            {
                return;
            }

            _running = true;

            if (_dispatchThread is null)
            {
                _dispatchThread = new Thread(DispatchLoop)
                {
                    IsBackground = true,
                    Name = "GhostClick.Hotkeys.Dispatch",
                };
                _dispatchThread.Start();
            }

            using var ready = new ManualResetEventSlim(false);
            Exception? startupError = null;

            _hookThread = new Thread(() => HookThreadMain(ready, ref startupError))
            {
                IsBackground = true,
                Name = "GhostClick.Hotkeys.Hook",
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(5)))
            {
                _running = false;
                throw new TimeoutException("The hotkey hook thread did not start in time.");
            }

            if (startupError is not null)
            {
                _running = false;
                throw startupError;
            }
        }
    }

    /// <summary>
    /// Uninstalls the hooks. The dispatch thread is left parked on the queue so the service
    /// can be started again - completing the queue would be permanent.
    /// </summary>
    public void Stop()
    {
        Thread? hookThread;

        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            hookThread = _hookThread;
            _hookThread = null;

            // A key held across a stop must not look pressed to the next run.
            _pressed.Clear();

            if (_hookThreadId != 0)
            {
                // Breaks the message loop, which then uninstalls the hooks on its own thread
                // (hooks must be removed by the thread that installed them).
                NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }

        hookThread?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();

        Thread? dispatchThread;
        lock (_sync)
        {
            dispatchThread = _dispatchThread;
            _dispatchThread = null;
        }

        _queue.CompleteAdding();
        dispatchThread?.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    /// <summary>Reads the modifiers currently held, straight from the OS.</summary>
    internal static KeyModifiers GetActiveModifiers()
    {
        var modifiers = KeyModifiers.None;

        if (IsDown(VirtualKey.Control))
        {
            modifiers |= KeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Alt))
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= KeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) => (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;

    private void HookThreadMain(ManualResetEventSlim ready, ref Exception? startupError)
    {
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            _keyboardProc = KeyboardHookProc;
            _mouseProc = MouseHookProc;

            var module = NativeMethods.GetModuleHandle(null);

            _keyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (_keyboardHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the keyboard hook.");
            }

            _mouseHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
            if (_mouseHook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install the mouse hook.");
            }
        }
        catch (Exception ex)
        {
            startupError = ex;
            SignalReady(ready);
            Uninstall();
            return;
        }

        SignalReady(ready);

        // Low-level hooks are only serviced while this thread pumps messages.
        while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        Uninstall();
    }

    /// <summary>
    /// A Start that timed out will already have disposed the handle; signalling it then is
    /// harmless, but an unhandled exception on this thread would not be.
    /// </summary>
    private static void SignalReady(ManualResetEventSlim ready)
    {
        try
        {
            ready.Set();
        }
        catch (ObjectDisposedException)
        {
            // The starter gave up waiting.
        }
    }

    private void Uninstall()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _keyboardProc = null;
        _mouseProc = null;
        _hookThreadId = 0;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != NativeMethods.HC_ACTION)
        {
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        int message = wParam.ToInt32();

        bool isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
        bool isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

        if ((isDown || isUp) && !IsSelfInjected(data.flags, NativeMethods.LLKHF_INJECTED, data.dwExtraInfo))
        {
            if (HandleKey((VirtualKey)data.vkCode, isDown) && SuppressHotkeys)
            {
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != NativeMethods.HC_ACTION)
        {
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        int message = wParam.ToInt32();
        if (!TryGetMouseKey(message, lParam, out var key, out bool isDown, out var data))
        {
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        if (!IsSelfInjected(data.flags, NativeMethods.LLMHF_INJECTED, data.dwExtraInfo) &&
            HandleKey(key, isDown) &&
            SuppressHotkeys)
        {
            return new IntPtr(1);
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static bool TryGetMouseKey(
        int message,
        IntPtr lParam,
        out VirtualKey key,
        out bool isDown,
        out NativeMethods.MSLLHOOKSTRUCT data)
    {
        data = default;
        key = VirtualKey.None;
        isDown = false;

        switch (message)
        {
            case NativeMethods.WM_LBUTTONDOWN:
                key = VirtualKey.LeftButton;
                isDown = true;
                break;
            case NativeMethods.WM_LBUTTONUP:
                key = VirtualKey.LeftButton;
                break;
            case NativeMethods.WM_RBUTTONDOWN:
                key = VirtualKey.RightButton;
                isDown = true;
                break;
            case NativeMethods.WM_RBUTTONUP:
                key = VirtualKey.RightButton;
                break;
            case NativeMethods.WM_MBUTTONDOWN:
                key = VirtualKey.MiddleButton;
                isDown = true;
                break;
            case NativeMethods.WM_MBUTTONUP:
                key = VirtualKey.MiddleButton;
                break;
            case NativeMethods.WM_XBUTTONDOWN:
            case NativeMethods.WM_XBUTTONUP:
                data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

                // The X button index lives in the high word of mouseData.
                uint index = (data.mouseData >> 16) & 0xFFFF;
                key = index == NativeMethods.XBUTTON2 ? VirtualKey.XButton2 : VirtualKey.XButton1;
                isDown = message == NativeMethods.WM_XBUTTONDOWN;
                return true;
            default:
                return false;
        }

        data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        return true;
    }

    /// <summary>True for events this process itself injected, which must never retrigger a hotkey.</summary>
    private bool IsSelfInjected(uint flags, uint injectedFlag, IntPtr extraInfo) =>
        (flags & injectedFlag) != 0 || (IgnoredExtraInfo != 0 && extraInfo.ToInt64() == IgnoredExtraInfo);

    /// <summary>
    /// Matches an observed key event against the bindings. Returns true when it matched, so
    /// the caller can decide whether to swallow it.
    /// </summary>
    private bool HandleKey(VirtualKey key, bool isDown)
    {
        HotkeyAction action;
        HotkeyBinding binding;

        lock (_sync)
        {
            if (isDown)
            {
                // The trigger key's own modifier flag must not count towards the required
                // modifiers, otherwise a binding on bare Shift could never match.
                var active = GetActiveModifiers() & ~key.ToModifierFlag();

                if (!TryMatch(key, active, out action, out binding))
                {
                    return false;
                }

                // Holding a key produces a stream of key-down messages; only the first is a
                // press as far as the hotkey is concerned.
                if (!_pressed.Add(action))
                {
                    return true;
                }
            }
            else
            {
                // Releases match on the key alone: by the time a chord is released the
                // modifiers may already be up.
                if (!TryMatchRelease(key, out action, out binding))
                {
                    return false;
                }

                if (!_pressed.Remove(action))
                {
                    return false;
                }
            }
        }

        _queue.TryAdd(new HotkeyEventArgs(action, binding, isDown));
        return true;
    }

    private bool TryMatch(VirtualKey key, KeyModifiers modifiers, out HotkeyAction action, out HotkeyBinding binding)
    {
        foreach (var (candidateAction, candidateBinding) in EnumerateBindings())
        {
            if (candidateBinding.Matches(key, modifiers))
            {
                action = candidateAction;
                binding = candidateBinding;
                return true;
            }
        }

        action = default;
        binding = new HotkeyBinding();
        return false;
    }

    private bool TryMatchRelease(VirtualKey key, out HotkeyAction action, out HotkeyBinding binding)
    {
        foreach (var (candidateAction, candidateBinding) in EnumerateBindings())
        {
            if (candidateBinding.IsBound && candidateBinding.Key == key && _pressed.Contains(candidateAction))
            {
                action = candidateAction;
                binding = candidateBinding;
                return true;
            }
        }

        action = default;
        binding = new HotkeyBinding();
        return false;
    }

    /// <summary>
    /// Bindings in priority order: the emergency stop is checked first so it wins any
    /// accidental overlap.
    /// </summary>
    private IEnumerable<(HotkeyAction Action, HotkeyBinding Binding)> EnumerateBindings()
    {
        yield return (HotkeyAction.EmergencyStop, _settings.EmergencyStop);
        yield return (HotkeyAction.StartStop, _settings.StartStop);
        yield return (HotkeyAction.Pause, _settings.Pause);
        yield return (HotkeyAction.RecordPoint, _settings.RecordPoint);
    }

    private void DispatchLoop()
    {
        try
        {
            foreach (var args in _queue.GetConsumingEnumerable())
            {
                try
                {
                    HotkeyTriggered?.Invoke(this, args);
                }
                catch (Exception)
                {
                    // A misbehaving handler must not take the hotkey pump down with it.
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Disposed while draining; nothing left to do.
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding raced with the enumerator; the queue is finished either way.
        }
    }
}
