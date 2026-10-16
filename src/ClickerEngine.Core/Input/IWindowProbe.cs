using System.Runtime.Versioning;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Native;

namespace ClickerEngine.Core.Input;

/// <summary>
/// Tells the engine which window its input would land in, so it can hold actions that would
/// go to the wrong place: the clicker's own window, or - with
/// <see cref="SafetySettings.ActiveWindowOnly"/> - any window other than the one the run
/// locked onto.
/// <para>
/// Windows are opaque handles. Zero means "unknown", and the engine never holds an action
/// because of an unknown window, so a probe that cannot answer degrades to no checks at all.
/// </para>
/// </summary>
public interface IWindowProbe
{
    /// <summary>The top-level window that receives keyboard input, or 0.</summary>
    long GetForegroundWindow();

    /// <summary>The top-level window under a screen point, i.e. where a click there lands, or 0.</summary>
    long GetWindowAt(ScreenPoint point);

    /// <summary>True when the window belongs to the process hosting the engine.</summary>
    bool IsOwnWindow(long window);
}

/// <summary>The Win32 implementation.</summary>
[SupportedOSPlatform("windows")]
public sealed class Win32WindowProbe : IWindowProbe
{
    private readonly uint _processId = (uint)Environment.ProcessId;

    public long GetForegroundWindow() => NativeMethods.GetForegroundWindow().ToInt64();

    public long GetWindowAt(ScreenPoint point)
    {
        var hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y });
        if (hit == IntPtr.Zero)
        {
            return 0;
        }

        // WindowFromPoint returns the innermost child; compare top-level windows.
        var root = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
        return (root == IntPtr.Zero ? hit : root).ToInt64();
    }

    public bool IsOwnWindow(long window)
    {
        if (window == 0)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(new IntPtr(window), out uint processId);
        return processId == _processId;
    }
}
