using System.Windows;
using System.Windows.Threading;

namespace ClickerEngine.DevUI;

/// <summary>
/// The dev harness entry point. Deliberately minimal: this project is scaffolding for
/// testing the engine by hand and is meant to be replaced wholesale by the real UI.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A clicker that dies silently while it holds a mouse button down is worse than one
        // that tells you what went wrong.
        MessageBox.Show(
            e.Exception.Message,
            "GhostClick dev harness",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
