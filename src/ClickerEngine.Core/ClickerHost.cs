using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;
using ClickerEngine.Core.Points;
using ClickerEngine.Core.Profiles;
using ClickerEngine.Core.Timing;

namespace ClickerEngine.Core;

/// <summary>
/// Assembles the engine, the hotkey listener, the profile store and the point recorder into
/// one object.
/// <para>
/// A front end needs nothing else: construct a host, call <see cref="Start"/>, bind to the
/// engine's events. The dev UI in this repository is built on exactly this surface, which is
/// the point - when the real UI arrives, it replaces the WPF project and keeps this API.
/// </para>
/// </summary>
public sealed class ClickerHost : IDisposable
{
    private bool _disposed;

    public ClickerHost(
        IInputInjector injector,
        IProfileStore profileStore,
        IHotkeyService hotkeyService,
        IClock? clock = null,
        IScreenGeometry? screen = null,
        IRandomSource? random = null)
    {
        Injector = injector ?? throw new ArgumentNullException(nameof(injector));
        ProfileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        Hotkeys = hotkeyService ?? throw new ArgumentNullException(nameof(hotkeyService));

        Screen = screen ?? CreateDefaultScreen();
        Engine = new AutoClickerEngine(injector, clock, Screen, random);
        Profiles = new ProfileManager(profileStore);
        Recorder = new PointRecorder(injector);
        Controller = new HotkeyController(Hotkeys, Engine, Recorder);
    }

    /// <summary>The input backend in use.</summary>
    public IInputInjector Injector { get; }

    /// <summary>Where profiles are persisted.</summary>
    public IProfileStore ProfileStore { get; }

    /// <summary>The global hotkey listener.</summary>
    public IHotkeyService Hotkeys { get; }

    /// <summary>The desktop bounds used for clamping.</summary>
    public IScreenGeometry Screen { get; }

    /// <summary>The run engine.</summary>
    public AutoClickerEngine Engine { get; }

    /// <summary>Profile loading and saving.</summary>
    public ProfileManager Profiles { get; }

    /// <summary>Cursor capture for macro routes.</summary>
    public PointRecorder Recorder { get; }

    /// <summary>Glue between hotkeys and the engine.</summary>
    public HotkeyController Controller { get; }

    /// <summary>
    /// The production wiring: <c>SendInput</c>, JSON profiles under <c>%APPDATA%\GhostClick</c>
    /// and low-level global hooks. Falls back to the dry-run recorder off Windows so the
    /// library is still constructible for tooling and tests.
    /// </summary>
    /// <param name="profileRoot">Overrides the profile directory.</param>
    /// <param name="dryRun">Record input instead of injecting it.</param>
    public static ClickerHost CreateDefault(string? profileRoot = null, bool dryRun = false)
    {
        var screen = CreateDefaultScreen();

        IInputInjector injector = dryRun || !OperatingSystem.IsWindows()
            ? new RecordingInputInjector()
            : new SendInputInjector(screen);

        IHotkeyService hotkeys = OperatingSystem.IsWindows()
            ? new GlobalHotkeyService()
            : new ManualHotkeyService();

        var store = profileRoot is null ? new JsonProfileStore() : new JsonProfileStore(profileRoot);

        return new ClickerHost(injector, store, hotkeys, screen: screen);
    }

    /// <summary>
    /// Loads the last used profile (creating a default when there is none), applies it and
    /// starts listening for hotkeys.
    /// </summary>
    public ClickerProfile Start()
    {
        var profile = Profiles.LoadLastUsedOrDefault();
        ApplyProfile(profile);
        Controller.Start();
        return profile;
    }

    /// <summary>
    /// Makes a profile the active one: hands it to the engine, republishes its hotkeys and
    /// loads its route into the recorder.
    /// </summary>
    public void ApplyProfile(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Engine.Profile = profile;
        Recorder.Load(profile.Target.Route);
        Controller.Reconfigure();
    }

    /// <summary>Loads a stored profile by name and applies it.</summary>
    public ClickerProfile LoadProfile(string name)
    {
        var profile = Profiles.Load(name);
        ApplyProfile(profile);
        return profile;
    }

    /// <summary>
    /// Copies the recorded points into the active profile and saves it. This is what the
    /// record hotkey feeds into.
    /// </summary>
    public void SaveProfile()
    {
        Recorder.ApplyTo(Profiles.Current);
        Profiles.Save();
        Controller.Reconfigure();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Controller.Dispose();
        Hotkeys.Dispose();
        Engine.Dispose();
    }

    private static IScreenGeometry CreateDefaultScreen() => OperatingSystem.IsWindows()
        ? new Win32ScreenGeometry()
        : new FixedScreenGeometry(1920, 1080);
}
