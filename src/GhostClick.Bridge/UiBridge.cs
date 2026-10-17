using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ClickerEngine.Core;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Planning;
using ClickerEngine.Core.Points;
using ClickerEngine.Core.Profiles;

namespace GhostClick.Bridge;

/// <summary>
/// The page side of the bridge: whatever hosts the web interface (a WebView2 window, a test).
/// </summary>
public interface IUiChannel
{
    /// <summary>Delivers one JSON message to the page. Only called on the UI thread.</summary>
    void Send(string json);

    /// <summary>Queues work onto the UI thread without waiting for it.</summary>
    void Post(Action action);
}

/// <summary>
/// Connects the web interface to a <see cref="ClickerHost"/>.
/// <para>
/// The page owns the editing: it holds the visible slice of the profile
/// (<see cref="ProfileDto"/>), applies the assignment rules, and sends the whole slice back
/// after every change. This class owns everything that has to be true outside the page:
/// the engine profile, the hotkeys, persistence, the unsaved-changes flag and the run
/// counters.
/// </para>
/// <para>
/// It also keeps the interface language (<see cref="UiSettings"/>, outside every profile) and
/// writes its own log lines and notices in it.
/// </para>
/// <para>
/// Every message in and out is handled on the UI thread. Engine and hotkey events arrive on
/// their own threads and are posted over with <see cref="IUiChannel.Post"/>, never invoked
/// synchronously: <see cref="AutoClickerEngine.Stop"/> blocks its caller until the run
/// thread finishes, so a synchronous hop back would deadlock.
/// </para>
/// </summary>
public sealed class UiBridge : IDisposable
{
    /// <summary>Dry-run events kept in memory; older ones are dropped.</summary>
    public const int DryRunEventCapacity = 2000;

    private const int MaxPendingLogs = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ClickerHost _host;
    private readonly SwitchableInputInjector _injector;
    private readonly IUiChannel _channel;
    private readonly IUiSettingsStore _settingsStore;
    private readonly Func<DateTime> _now;
    private readonly Stopwatch _runTime = new();
    private readonly List<object> _pendingLogs = new();

    private UiSettings _settings = new();
    private UiText _text = UiText.Russian;
    private string _savedJson = string.Empty;
    private string _sentIssues = string.Empty;
    private ScriptLocation? _sentBlock;
    private int _blockFlushPending;
    private int _runTimeLimitSeconds;
    private bool _started;
    private bool _ready;
    private bool _listening;
    private bool _hotkeysAvailable = true;
    private bool _disposed;

    /// <summary>
    /// <paramref name="settings"/> is where the interface language is kept between runs; in
    /// memory when omitted.
    /// </summary>
    public UiBridge(
        ClickerHost host,
        SwitchableInputInjector injector,
        IUiChannel channel,
        Func<DateTime>? now = null,
        IUiSettingsStore? settings = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _settingsStore = settings ?? new InMemoryUiSettingsStore();
        _now = now ?? (() => DateTime.Now);
    }

    /// <summary>The interface language: <c>ru</c> or <c>en</c>.</summary>
    public string Language => _text.Language;

    private ClickerProfile Current => _host.Profiles.Current;

    private bool IsDirty => ProfileSerializer.Serialize(Current) != _savedJson;

    /// <summary>
    /// Loads the last used profile and starts the global hotkeys. A hotkey failure is
    /// reported in the log rather than thrown: the interface still works without them.
    /// </summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _settings = _settingsStore.Load();
        _text = UiText.For(_settings.Language);
        _host.Profiles.NewProfileFactory = UiDefaults.CreateProfile;
        _injector.Recorder.Capacity = DryRunEventCapacity;
        Subscribe();

        try
        {
            _host.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _hotkeysAvailable = false;
            Log(_text.HotkeysUnavailable + ex.Message);
        }

        ApplyToEngine();
        _savedJson = ProfileSerializer.Serialize(Current);
        Log(_text.ProfileLoaded(Current.Name));
    }

    /// <summary>Handles one message from the page. Malformed messages are ignored.</summary>
    public void Receive(string json)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String)
            {
                return;
            }

            Dispatch(type.GetString()!, message);
        }
        catch (JsonException)
        {
            // Not a message we understand; the page and the host are out of step, and there
            // is nobody to report it to but the page that sent it.
        }
    }

    /// <summary>Called by the host a few times a second; pushes live counters while a run is active.</summary>
    public void Tick()
    {
        if (_host.Engine.IsActive)
        {
            SendState();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_started)
        {
            Unsubscribe();
        }
    }

    private void Dispatch(string type, JsonElement message)
    {
        switch (type)
        {
            case "ready":
                OnReady();
                break;
            case "start":
                StartRun();
                break;
            case "stop":
                _host.Engine.Stop();
                break;
            case "pause":
                _host.Engine.Pause();
                break;
            case "resume":
                _host.Engine.Resume();
                break;
            case "panic":
                _host.Engine.EmergencyStop();
                break;
            case "setProfile":
                if (message.TryGetProperty("data", out var data))
                {
                    SetProfile(data.Deserialize<ProfileDto>(Json));
                }

                break;
            case "saveProfile":
                SaveProfile();
                break;
            case "revertProfile":
                RevertProfile();
                break;
            case "loadProfile":
                LoadProfile(ReadString(message, "name"));
                break;
            case "newProfile":
                NewProfile(ReadString(message, "kind"));
                break;
            case "duplicateProfile":
                DuplicateProfile();
                break;
            case "setDryRun":
                SetDryRun(ReadBool(message, "value"));
                break;
            case "listen":
                SetListening(ReadBool(message, "value"));
                break;
            case "setLanguage":
                SetLanguage(ReadString(message, "lang"));
                break;
        }
    }

    // ------------------------------------------------------------------ page lifecycle

    private void OnReady()
    {
        _ready = true;

        // A page that just (re)loaded is not in the middle of assigning a key.
        if (_listening)
        {
            SetListening(false);
        }

        _sentBlock = _host.Engine.CurrentBlock;
        Send(new
        {
            type = "init",
            profiles = _host.Profiles.AvailableProfiles,
            profile = ProfilePayload(),
            runtime = StatePayload(),
            dryRun = _injector.DryRun,
            backend = _injector.Name,
            hotkeys = _hotkeysAvailable,
            lang = _text.Language,
        });

        foreach (var entry in _pendingLogs)
        {
            Send(entry);
        }

        _pendingLogs.Clear();
    }

    // ------------------------------------------------------------------ running

    private void StartRun()
    {
        if (_host.Engine.IsActive)
        {
            return;
        }

        try
        {
            _host.Engine.Start();
        }
        catch (ProfileValidationException ex)
        {
            Toast(Describe(ex, Current.Script), error: true);
        }
    }

    private void SetDryRun(bool value)
    {
        if (_injector.DryRun == value)
        {
            return;
        }

        // Switching mid-run could press a button on one backend and release it on the
        // other, leaving it stuck down for the whole system. Stop first.
        if (_host.Engine.IsActive)
        {
            _host.Engine.Stop();
        }

        _injector.DryRun = value;
        _injector.ResetDryRunCursor();
    }

    // ------------------------------------------------------------------ language

    /// <summary>
    /// The page has switched languages: from now on the log and notices follow, and the choice
    /// is kept for the next start. Lines already in the log stay as they were written.
    /// </summary>
    private void SetLanguage(string? language)
    {
        if (!UiText.IsLanguage(language) || language == _text.Language)
        {
            return;
        }

        _text = UiText.For(language);
        _settings.Language = _text.Language;

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(_text.LanguageNotSaved + ex.Message);
        }
    }

    // ------------------------------------------------------------------ editing

    private void SetProfile(ProfileDto? dto)
    {
        if (dto is null)
        {
            return;
        }

        ProfileMapper.Apply(dto, Current);
        ApplyToEngine();
        Send(new { type = "dirty", value = IsDirty });
        SendIssuesIfChanged();
    }

    /// <summary>
    /// While the page waits for a key to assign, the hotkeys must not fire: pressing F6 to
    /// bind it to «Пауза» should not also start a run. The emergency stop stays live.
    /// </summary>
    private void SetListening(bool value)
    {
        _listening = value;

        if (value)
        {
            SuspendHotkeys();
        }
        else
        {
            _host.Controller.Reconfigure();
        }
    }

    /// <summary>
    /// Hands the engine its copy of the current profile (see
    /// <see cref="ProfileMapper.ToEngineProfile"/>). The profile being edited, saved and
    /// compared for unsaved changes stays <see cref="Current"/>.
    /// </summary>
    private void ApplyToEngine()
    {
        _host.ApplyProfile(ProfileMapper.ToEngineProfile(Current));

        // ApplyProfile republishes every hotkey; keep them quiet while the page is listening.
        if (_listening)
        {
            SuspendHotkeys();
        }
    }

    private void SuspendHotkeys() => _host.Hotkeys.Configure(new HotkeySettings
    {
        StartStop = new HotkeyBinding(),
        Pause = new HotkeyBinding(),
        RecordPoint = new HotkeyBinding(),
        EmergencyStop = Current.Hotkeys.EmergencyStop.Clone(),
    });

    // ------------------------------------------------------------------ profiles

    private void SaveProfile()
    {
        var name = Current.Name;

        try
        {
            _host.SaveProfile();
        }
        catch (ProfileValidationException ex)
        {
            Toast(Describe(ex, Current.Script), error: true);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast(_text.SaveFailed + ex.Message, error: true);
            return;
        }
        finally
        {
            if (_listening)
            {
                SuspendHotkeys();
            }
        }

        _savedJson = ProfileSerializer.Serialize(Current);
        SendProfile();
        Toast(_text.ProfileSaved(name));
        Log(_text.ProfileSaved(name));
    }

    private void RevertProfile() => Reload(Current.Name, stopRun: false);

    private void LoadProfile(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, Current.Name, StringComparison.Ordinal))
        {
            return;
        }

        Reload(name, stopRun: true);
    }

    private void Reload(string name, bool stopRun)
    {
        // A run belongs to the profile it started with; carrying it over to another
        // profile would also swap its stop hotkey out from under it.
        if (stopRun && _host.Engine.IsActive)
        {
            _host.Engine.Stop();
        }

        try
        {
            _host.LoadProfile(name);
        }
        catch (Exception ex) when (ex is ProfileNotFoundException or ProfileFormatException or IOException)
        {
            Toast(_text.LoadFailed(name, ex.Message), error: true);
            SendProfile();
            return;
        }

        AfterProfileSwap();
        Log(_text.ProfileLoaded(Current.Name));
    }

    /// <summary>A new clicker, or with <paramref name="kind"/> <c>script</c>, a new script.</summary>
    private void NewProfile(string? kind)
    {
        if (_host.Engine.IsActive)
        {
            _host.Engine.Stop();
        }

        bool script = kind == "script";
        var name = UniqueName(script ? _text.NewScriptName : _text.NewProfileName, _host.Profiles.AvailableProfiles.Count + 1);

        try
        {
            _host.Profiles.CreateNew(name, script ? UiDefaults.CreateScript : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast(_text.CreateFailed + ex.Message, error: true);
            return;
        }

        AfterProfileSwap();
        Log(_text.ProfileCreated(name));
    }

    private void DuplicateProfile()
    {
        var source = Current.Name;

        // Check first: SaveAs swaps the copy in before it validates, and a copy that fails
        // to save would be left current without existing anywhere.
        var errors = ProfileValidator.Validate(Current);
        if (errors.Count > 0)
        {
            Toast(Describe(errors, Current.Script), error: true);
            return;
        }

        var name = UniqueName(i => _text.CopyName(source, i), 1);

        try
        {
            _host.Recorder.ApplyTo(Current);
            _host.Profiles.SaveAs(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast(_text.CopyFailed + ex.Message, error: true);
            return;
        }

        AfterProfileSwap();
        Log(_text.ProfileCopied(name, source));
    }

    private void AfterProfileSwap()
    {
        ApplyToEngine();
        _savedJson = ProfileSerializer.Serialize(Current);
        SendProfile();
        SendState();
    }

    private string UniqueName(Func<int, string> candidate, int first)
    {
        for (int i = first; ; i++)
        {
            var name = candidate(i);
            if (!_host.ProfileStore.Exists(name))
            {
                return name;
            }
        }
    }

    // ------------------------------------------------------------------ engine events

    private void Subscribe()
    {
        _host.Engine.StateChanged += OnStateChanged;
        _host.Engine.Started += OnStarted;
        _host.Engine.Stopped += OnStopped;
        _host.Engine.BlockStarted += OnBlockStarted;
        _host.Controller.PointRecorded += OnPointRecorded;
        _host.Controller.StartRejected += OnStartRejected;
        _host.Hotkeys.HotkeyTriggered += OnHotkeyTriggered;
    }

    private void Unsubscribe()
    {
        _host.Engine.StateChanged -= OnStateChanged;
        _host.Engine.Started -= OnStarted;
        _host.Engine.Stopped -= OnStopped;
        _host.Engine.BlockStarted -= OnBlockStarted;
        _host.Controller.PointRecorded -= OnPointRecorded;
        _host.Controller.StartRejected -= OnStartRejected;
        _host.Hotkeys.HotkeyTriggered -= OnHotkeyTriggered;
    }

    private void OnStateChanged(object? sender, EngineStateChangedEventArgs e) => _channel.Post(() =>
    {
        if (e.State == EngineState.Running)
        {
            _runTime.Start();
        }
        else
        {
            _runTime.Stop();
        }

        if (e.State == EngineState.Paused)
        {
            Log(_text.Paused);
        }
        else if (e.PreviousState == EngineState.Paused && e.State == EngineState.Running)
        {
            Log(_text.Resumed);
        }

        SendState();
    });

    private void OnStarted(object? sender, EngineStartedEventArgs e)
    {
        // Runs on the thread that started the engine, before the run thread exists, so the
        // dry-run cursor is re-seeded before the plan first asks where the cursor is.
        _injector.ResetDryRunCursor();
        _injector.Recorder.Clear();

        int? scriptBlocks = e.Profile.Kind == ProfileKind.Script ? ScriptSettings.Walk(e.Profile.Script.Blocks).Count() : null;
        var inputs = e.Profile.Action.Inputs.Select(i => (i.Key, i.Modifiers)).ToArray();
        bool dryRun = _injector.DryRun;
        int timeLimit = e.Profile.Safety.MaxRunDurationSeconds;

        _channel.Post(() =>
        {
            _runTimeLimitSeconds = timeLimit;
            _runTime.Restart();

            var actions = scriptBlocks is { } blocks
                ? _text.ScriptSummary(blocks)
                : string.Join(" + ", inputs.Select(i => KeyCodes.Describe(i.Key, i.Modifiers, _text.Language)));
            Log($"{_text.Started} · {(actions.Length > 0 ? actions : _text.NoActions)}{(dryRun ? " · dry run" : string.Empty)}");
            SendState();
        });
    }

    private void OnStopped(object? sender, EngineStoppedEventArgs e) => _channel.Post(() =>
    {
        _runTime.Stop();
        SendBlock(null);
        Log($"{StopReasonText(e)} · {e.ActionsPerformed} {_text.Clicks(e.ActionsPerformed)}");

        if (e.Reason == StopReason.Error)
        {
            Toast(e.Error?.Message ?? _text.StoppedByError, error: true);
        }

        SendState();
    });

    private void OnPointRecorded(object? sender, PointRecordedEventArgs e) =>
        _channel.Post(() => Send(new { type = "pointAdded", x = e.Point.X, y = e.Point.Y }));

    private void OnStartRejected(object? sender, ProfileValidationException e) =>
        _channel.Post(() => Toast(Describe(e, Current.Script), error: true));

    /// <summary>
    /// Blocks can change far faster than the page can draw them, so this posts one flush at a
    /// time, which sends whichever block is current by the time it runs.
    /// </summary>
    private void OnBlockStarted(object? sender, BlockStartedEventArgs e)
    {
        if (Interlocked.Exchange(ref _blockFlushPending, 1) == 0)
        {
            _channel.Post(() =>
            {
                Interlocked.Exchange(ref _blockFlushPending, 0);
                SendBlock(_host.Engine.CurrentBlock);
            });
        }
    }

    private void OnHotkeyTriggered(object? sender, HotkeyEventArgs e)
    {
        var slot = e.Action switch
        {
            HotkeyAction.StartStop => "toggle",
            HotkeyAction.Pause => "pause",
            HotkeyAction.EmergencyStop => "panic",
            _ => "point",
        };

        _channel.Post(() => Send(new { type = "hotkey", slot, down = e.IsPressed }));
    }

    // ------------------------------------------------------------------ outgoing

    private object ProfilePayload() => new
    {
        name = Current.Name,
        data = ProfileMapper.ToDto(Current),
        dirty = IsDirty,
        issues = TrackIssues(),
    };

    /// <summary>The current profile's problems, remembered as the last the page was told about.</summary>
    private List<IssueDto> TrackIssues()
    {
        var script = Current.Script;
        var issues = ProfileValidator.Validate(Current)
            .Select(e => ScriptTexts.Issue(e, ScriptTexts.Message(e, script) ?? Describe(new[] { e })))
            .ToList();

        _sentIssues = JsonSerializer.Serialize(issues, Json);
        return issues;
    }

    /// <summary>After an edit: tells the page about its problems, when they changed.</summary>
    private void SendIssuesIfChanged()
    {
        var sent = _sentIssues;
        var issues = TrackIssues();
        if (_sentIssues != sent)
        {
            Send(new { type = "issues", issues });
        }
    }

    private void SendBlock(ScriptLocation? location)
    {
        if (ReferenceEquals(location, _sentBlock))
        {
            return;
        }

        _sentBlock = location;
        Send(new { type = "block", block = BlockPayload(location) });
    }

    private static object? BlockPayload(ScriptLocation? location) => location is null ? null : new
    {
        id = location.BlockId,
        pass = location.Pass,
        passes = location.Passes,
        loops = location.Loops.Select(l => new { id = l.BlockId, iteration = l.Iteration, count = l.Count }),
    };

    private void SendProfile() => Send(new
    {
        type = "profile",
        profiles = _host.Profiles.AvailableProfiles,
        profile = ProfilePayload(),
    });

    private object StatePayload()
    {
        var engine = _host.Engine;
        long clicks = engine.CurrentRunActions;

        return new
        {
            type = "state",
            state = engine.State switch
            {
                EngineState.Running => "running",
                EngineState.Paused => "paused",
                _ => "stopped",
            },
            clicks,
            elapsedMs = (long)_runTime.Elapsed.TotalMilliseconds,
            pointIndex = NextPointIndex(clicks),
            block = BlockPayload(engine.CurrentBlock),
        };
    }

    private void SendState() => Send(StatePayload());

    /// <summary>Index into the route of the point the next click goes to, or -1.</summary>
    private int NextPointIndex(long actions)
    {
        if (Current.Kind == ProfileKind.Script)
        {
            return -1;
        }

        var route = Current.Target.Route;
        var enabled = Enumerable.Range(0, route.Count).Where(i => route[i].Enabled).ToArray();
        return enabled.Length == 0 ? -1 : enabled[(int)(actions % enabled.Length)];
    }

    private void Log(string message)
    {
        var entry = new
        {
            type = "log",
            time = _now().ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            message,
        };

        if (_ready)
        {
            Send(entry);
        }
        else if (_pendingLogs.Count < MaxPendingLogs)
        {
            _pendingLogs.Add(entry);
        }
    }

    private void Toast(string message, bool error = false) =>
        Send(new { type = "toast", message, error });

    private void Send(object payload)
    {
        if (_disposed || !_ready)
        {
            return;
        }

        _channel.Send(JsonSerializer.Serialize(payload, Json));
    }

    // ------------------------------------------------------------------ text

    private string StopReasonText(EngineStoppedEventArgs e) => e.Reason switch
    {
        StopReason.User => _text.StopUser,
        StopReason.Completed => _text.StopCompleted,
        StopReason.HoldReleased => _text.StopHoldReleased,
        StopReason.Failsafe => _text.StopFailsafe,
        // The limit of the run that stopped, not of the profile as it is now edited.
        StopReason.TimeLimit => _text.StopTimeLimit(_text.Duration(_runTimeLimitSeconds)),
        StopReason.ActionLimit => _text.StopActionLimit,
        StopReason.UserCursorMoved => _text.StopCursorMoved,
        StopReason.Error => _text.StopError(e.Error?.Message ?? _text.Unknown),
        _ => _text.StopUser,
    };

    private string Describe(ProfileValidationException exception, ScriptSettings? script = null) =>
        Describe(exception.Errors, _text, script);

    private string Describe(IReadOnlyList<ValidationError> errors, ScriptSettings? script = null) =>
        Describe(errors, _text, script);

    /// <summary>
    /// The first validation problem, in the interface's words where there are some. A problem
    /// in a script block names the block, which is why the script is wanted.
    /// </summary>
    public static string Describe(ProfileValidationException exception, UiText text, ScriptSettings? script = null) =>
        Describe(exception.Errors, text, script);

    /// <inheritdoc cref="Describe(ProfileValidationException, UiText, ScriptSettings?)"/>
    public static string Describe(IReadOnlyList<ValidationError> errors, UiText text, ScriptSettings? script = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(text);

        if (errors.Count == 0)
        {
            return text.ProfileHasErrors;
        }

        var error = errors[0];
        if (ScriptTexts.Describe(error, script) is { } scriptProblem)
        {
            return scriptProblem;
        }

        return error.Path switch
        {
            "Target.Route" => text.NeedsPoint,
            "Action.Inputs" => text.NeedsAction,
            "Interval.DelayMs" => text.NegativeDelay,
            "Interval.MinDelayMs" => text.NegativeMinDelay,
            "Interval.MaxDelayMs" => text.MaxBelowMin,
            "Repeat.Count" => text.CountBelowOne,
            "Hotkeys.StartStop" => text.HoldNeedsStartKey,
            "Name" => text.NeedsName,
            _ when error.Path.StartsWith("Action.Inputs[", StringComparison.Ordinal) && error.Path.EndsWith(".PressMs", StringComparison.Ordinal) => text.NegativePress,
            _ when error.Path.StartsWith("Action.Inputs[", StringComparison.Ordinal) && error.Path.EndsWith(".HoldMs", StringComparison.Ordinal) => text.HoldTooShort,
            _ => error.Message,
        };
    }

    private static string? ReadString(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
