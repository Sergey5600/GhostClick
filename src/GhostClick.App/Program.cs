using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GhostClick.App;

/// <summary>
/// Entry point. There is no App.xaml: the whole interface is a web page inside
/// <see cref="MainWindow"/>, so the WPF side is just a window and a message pump.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main()
    {
        // Two copies would install two sets of global hooks, and one F6 press would start
        // and immediately stop the clicker.
        using var instance = new Mutex(initiallyOwned: true, @"Local\GhostClick.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("GhostClick уже запущен.", "GhostClick", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += OnDispatcherUnhandledException;
        int exitCode = app.Run(new MainWindow());

        // An update waits for GhostClick to be gone: the window, the engine and the hooks are
        // by now, and the mutex the installer checks for goes before it starts.
        if (PendingUpdate is { } update)
        {
            instance.ReleaseMutex();
            instance.Dispose();
            RunInstaller(update.SetupPath, update.Relaunch);
        }

        return exitCode;
    }

    /// <summary>A downloaded installer to run once the app has closed (see <see cref="MainWindow"/>).</summary>
    internal static (string SetupPath, bool Relaunch)? PendingUpdate { get; set; }

    /// <summary>
    /// Runs the installer the way an upgrade should go: no questions, a progress window, and
    /// with <paramref name="relaunch"/> GhostClick started again at the end. /UPDATE makes it
    /// wait for this process to let go of the app first (installer/GhostClick.iss).
    /// </summary>
    private static void RunInstaller(string setupPath, bool relaunch)
    {
        if (!File.Exists(setupPath))
        {
            return;
        }

        var arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1" + (relaunch ? " /RELAUNCH=1" : string.Empty);
        try
        {
            Process.Start(new ProcessStartInfo(setupPath, arguments) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(ex.Message, "GhostClick", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A clicker that dies silently while it holds a mouse button down is worse than one
        // that tells you what went wrong.
        MessageBox.Show(e.Exception.Message, "GhostClick", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
