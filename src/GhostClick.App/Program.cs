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
        return app.Run(new MainWindow());
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A clicker that dies silently while it holds a mouse button down is worse than one
        // that tells you what went wrong.
        MessageBox.Show(e.Exception.Message, "GhostClick", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
