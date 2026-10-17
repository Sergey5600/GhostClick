using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClickerEngine.Core;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Profiles;
using GhostClick.Bridge;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GhostClick.App;

/// <summary>
/// One window, one WebView2 filling it. The page in <c>wwwroot</c> is the interface;
/// <see cref="UiBridge"/> connects it to the engine. This class only wires the two together
/// and keeps the browser locked down to that one page.
/// </summary>
public sealed class MainWindow : Window, IUiChannel
{
    /// <summary>
    /// The page is served from this made-up host, mapped onto the <c>wwwroot</c> folder. The
    /// reserved <c>.example</c> domain can never resolve to anything real.
    /// </summary>
    private const string PageHost = "ghostclick.example";

    private static readonly Uri PageUri = new($"https://{PageHost}/index.html");

    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private readonly WebView2 _webView = new();
    private readonly SwitchableInputInjector _injector;
    private readonly ClickerHost _host;
    private readonly UiBridge _bridge;
    private readonly DispatcherTimer _ticker;

    public MainWindow()
    {
        Title = "GhostClick";

        // 1440x860 where there is room, but never larger than the screen: on a 1366x768
        // laptop the window would otherwise open with its bottom (the dock) cut off. The page
        // scales itself to whatever size the window gets.
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(960, workArea.Width);
        MinHeight = Math.Min(580, workArea.Height);
        Width = Math.Max(MinWidth, Math.Min(1440, workArea.Width * 0.92));
        Height = Math.Max(MinHeight, Math.Min(860, workArea.Height * 0.92));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Black;

        // The .exe carries the icon too (ApplicationIcon), but a window shows its own: without
        // this the title bar would fall back to the generic one when run through dotnet run.
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/AppIcon.ico", UriKind.Absolute));

        // No white flash while the page loads into a black window.
        _webView.DefaultBackgroundColor = System.Drawing.Color.Black;
        Content = _webView;

        var screen = new Win32ScreenGeometry();
        var live = new SendInputInjector(screen);
        _injector = new SwitchableInputInjector(live, new RecordingInputInjector());

        var hotkeys = new GlobalHotkeyService
        {
            // The clicker's own clicks must not retrigger a hotkey bound to a mouse button.
            IgnoredExtraInfo = live.ExtraInfoSignature,
        };

        // Holds clicks that would land on this window (start a run with the button, then
        // move to the target) and powers «Только активное окно». Silent in dry run.
        var windowProbe = new DryRunAwareWindowProbe(new Win32WindowProbe(), _injector);

        _host = new ClickerHost(_injector, new JsonProfileStore(), hotkeys, screen: screen, windowProbe: windowProbe);
        _bridge = new UiBridge(_host, _injector, this, settings: new JsonUiSettingsStore());

        _ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += (_, _) => _bridge.Tick();

        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += OnLoaded;
        Closed += OnClosed;

        // A crash must not leave keys the clicker holds down pressed for the whole system:
        // stopping the engine releases them before the process goes.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _host.Engine.Stop(StopReason.Error);
    }

    // ------------------------------------------------------------------ IUiChannel

    void IUiChannel.Send(string json) => _webView.CoreWebView2?.PostWebMessageAsJson(json);

    // BeginInvoke, never Invoke: the engine's Stop blocks the UI thread until the run thread
    // ends, and a run thread waiting on the UI thread would never end.
    void IUiChannel.Post(Action action) => Dispatcher.BeginInvoke(DispatcherPriority.Normal, action);

    // ------------------------------------------------------------------ lifecycle

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loads the profile and starts the hotkeys now; anything it logs waits for the page.
        _bridge.Start();

        try
        {
            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GhostClick",
                "WebView2");

            var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: dataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ReportMissingRuntime();
            return;
        }

        var core = _webView.CoreWebView2;
        ConfigureBrowser(core);

        core.SetVirtualHostNameToFolderMapping(
            PageHost,
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            CoreWebView2HostResourceAccessKind.Deny);

        core.WebMessageReceived += (_, args) => _bridge.Receive(args.WebMessageAsJson);
        core.NavigationStarting += (_, args) => args.Cancel = !IsPage(args.Uri);
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.ProcessFailed += OnProcessFailed;

        core.Navigate(PageUri.AbsoluteUri);
        _ticker.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _ticker.Stop();
        _bridge.Dispose();

        // Stops a running clicker (releasing anything it holds down) and removes the hooks.
        _host.Dispose();
        _webView.Dispose();
    }

    /// <summary>
    /// The page is an application, not a website: no browser shortcuts (F5 would reload it,
    /// F12 open dev tools, Ctrl+P print), no zoom (it scales itself), no autofill, no swipe
    /// navigation. Keys still reach the page, which is how they get assigned.
    /// </summary>
    private static void ConfigureBrowser(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsWebMessageEnabled = true;

#if DEBUG
        // Right click, Inspect.
        settings.AreDevToolsEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
#endif
    }

    private static bool IsPage(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var target) &&
        target.Scheme == Uri.UriSchemeHttps &&
        string.Equals(target.Host, PageHost, StringComparison.OrdinalIgnoreCase);

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        // A crashed or hung page comes back on its own: it reloads, says "ready", and the
        // bridge sends it the current state. The engine and hotkeys never stopped.
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            _webView.CoreWebView2?.Reload();
        }
    }

    private void ReportMissingRuntime()
    {
        var answer = MessageBox.Show(
            this,
            "Для интерфейса GhostClick нужен Microsoft Edge WebView2 Runtime. " +
            "В Windows 11 он уже есть; в Windows 10 его можно установить с сайта Microsoft.\n\n" +
            "Открыть страницу загрузки?",
            "GhostClick",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });
        }

        Close();
    }

    // ------------------------------------------------------------------ dark title bar

    private const int DwmUseImmersiveDarkMode = 20;

    /// <summary>A black interface under a white title bar looks broken; ask DWM for the dark one.</summary>
    private void UseDarkTitleBar()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int enabled = 1;

        // Not available before Windows 10 20H1; the window simply keeps a light title bar.
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
