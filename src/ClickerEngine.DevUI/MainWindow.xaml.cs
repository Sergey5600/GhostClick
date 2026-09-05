using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClickerEngine.Core;
using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Engine;
using ClickerEngine.Core.Hotkeys;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Points;
using ClickerEngine.Core.Profiles;

namespace ClickerEngine.DevUI;

/// <summary>
/// A throwaway harness for driving the engine by hand during development.
/// <para>
/// It is intentionally plain code-behind with no MVVM framework and no bindings: everything
/// it knows about the engine goes through <see cref="ClickerHost"/>, so replacing this window
/// with the real UI means deleting this project, not untangling it.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Above this many entries the log starts dropping its oldest lines.</summary>
    private const int MaxLogEntries = 400;

    /// <summary>Backpressure: never let the engine thread outrun the UI thread's log queue.</summary>
    private const int MaxPendingLogEntries = 50;

    private readonly ClickerHost _host;
    private readonly SwitchableInputInjector _injector;
    private readonly DispatcherTimer _statusTimer;

    private volatile bool _logInjectedEvents;
    private int _pendingLogEntries;
    private bool _loadingProfileIntoUi;

    public MainWindow()
    {
        InitializeComponent();

        var screen = new Win32ScreenGeometry();
        var live = new SendInputInjector(screen);
        _injector = new SwitchableInputInjector(live, new RecordingInputInjector());

        var hotkeys = new GlobalHotkeyService
        {
            // Ignore the input this process injects, so a hotkey bound to a mouse button
            // cannot be retriggered by the clicker's own clicks.
            IgnoredExtraInfo = live.ExtraInfoSignature,
        };

        _host = new ClickerHost(_injector, new JsonProfileStore(), hotkeys, screen: screen);

        PopulateChoices();
        SubscribeToEngine();

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _statusTimer.Tick += (_, _) => RefreshStatus();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = _host.Start();
            Log($"Loaded profile '{profile.Name}'.");
        }
        catch (Exception ex)
        {
            Log("Could not start the hotkey listener: " + ex.Message);
            Log("The buttons still work; global hotkeys do not.");
        }

        RefreshProfileList();
        LoadProfileIntoUi(_host.Profiles.Current);
        RefreshRouteList();
        UpdateStateUi(_host.Engine.State);

        BackendText.Text = "Input: " + _injector.Name;
        _statusTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _statusTimer.Stop();
        _host.Dispose();
    }

    // ---------------------------------------------------------------- engine wiring

    private void SubscribeToEngine()
    {
        var engine = _host.Engine;

        // Every handler marshals with BeginInvoke rather than Invoke. Invoke would block the
        // engine thread, and Stop() blocks the UI thread waiting for that same engine thread:
        // the pair would deadlock.
        engine.StateChanged += (_, args) => Post(() => UpdateStateUi(args.State));

        engine.Started += (_, args) => Post(() => Log(
            args.PlannedActions is { } planned
                ? $"Started '{args.Profile.Name}': {planned} actions planned."
                : $"Started '{args.Profile.Name}': running until stopped."));

        engine.Stopped += (_, args) => Post(() =>
        {
            Log($"Stopped ({args.Reason}) after {args.ActionsPerformed} actions in {args.Duration.TotalSeconds:0.0} s.");

            if (args.Error is not null)
            {
                Log("Error: " + args.Error.Message);
            }
        });

        engine.ActionExecuted += (_, args) =>
        {
            if (_logInjectedEvents)
            {
                Post(() => Log($"#{args.ActionIndex} {args.Kind} at {args.Position}"));
            }
        };

        _injector.Recorder.EventRecorded += (_, injected) =>
        {
            if (_logInjectedEvents)
            {
                Post(() => Log("  " + injected));
            }
        };

        _host.Controller.PointRecorded += (_, args) => Post(() =>
        {
            Log($"Recorded point {args.Index + 1}: {args.Point}");
            RefreshRouteList();
        });

        LogInputBox.Checked += (_, _) => _logInjectedEvents = true;
        LogInputBox.Unchecked += (_, _) => _logInjectedEvents = false;
    }

    /// <summary>Queues work onto the UI thread, dropping it if the queue is already backed up.</summary>
    private void Post(Action action)
    {
        if (Interlocked.Increment(ref _pendingLogEntries) > MaxPendingLogEntries)
        {
            Interlocked.Decrement(ref _pendingLogEntries);
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                Interlocked.Decrement(ref _pendingLogEntries);
                action();
            }));
    }

    // ---------------------------------------------------------------- commands

    private void OnStartClicked(object sender, RoutedEventArgs e)
    {
        if (!TryApplyUi())
        {
            return;
        }

        _injector.ResetDryRunCursor();

        try
        {
            _host.Engine.Start();
        }
        catch (ProfileValidationException ex)
        {
            ShowValidationErrors(ex);
        }
    }

    private void OnStopClicked(object sender, RoutedEventArgs e) => _host.Engine.Stop();

    private void OnPauseClicked(object sender, RoutedEventArgs e) => _host.Engine.TogglePause();

    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (TryApplyUi())
        {
            Log("Applied the settings to the engine profile.");
        }
    }

    private void OnResetCountersClicked(object sender, RoutedEventArgs e)
    {
        _host.Engine.Statistics.Reset();
        RefreshStatus();
    }

    private void OnDryRunChanged(object sender, RoutedEventArgs e)
    {
        _injector.DryRun = DryRunBox.IsChecked == true;
        _injector.ResetDryRunCursor();
        BackendText.Text = "Input: " + _injector.Name;
        Log(_injector.DryRun ? "Dry run on: nothing reaches the system." : "Dry run off: input is real.");
    }

    private void OnClearLogClicked(object sender, RoutedEventArgs e) => LogList.Items.Clear();

    // ---------------------------------------------------------------- profiles

    private void OnLoadProfileClicked(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not string name)
        {
            return;
        }

        try
        {
            var profile = _host.LoadProfile(name);
            LoadProfileIntoUi(profile);
            RefreshRouteList();
            Log($"Loaded profile '{profile.Name}'.");
        }
        catch (Exception ex) when (ex is ProfileNotFoundException or ProfileFormatException)
        {
            Log("Could not load the profile: " + ex.Message);
        }
    }

    private void OnSaveProfileClicked(object sender, RoutedEventArgs e)
    {
        if (!TryApplyUi())
        {
            return;
        }

        try
        {
            _host.SaveProfile();
            RefreshProfileList();
            Log($"Saved profile '{_host.Profiles.Current.Name}'.");
        }
        catch (ProfileValidationException ex)
        {
            ShowValidationErrors(ex);
        }
    }

    private void OnSaveAsClicked(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Log("Type a name in the profile name box first.");
            return;
        }

        if (!TryApplyUi())
        {
            return;
        }

        try
        {
            _host.Recorder.ApplyTo(_host.Profiles.Current);
            var profile = _host.Profiles.SaveAs(name);
            _host.ApplyProfile(profile);
            RefreshProfileList();
            LoadProfileIntoUi(profile);
            Log($"Saved a copy as '{profile.Name}'.");
        }
        catch (ProfileValidationException ex)
        {
            ShowValidationErrors(ex);
        }
    }

    private void OnNewProfileClicked(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || _host.ProfileStore.Exists(name))
        {
            name = NextAvailableProfileName();
        }

        var profile = _host.Profiles.CreateNew(name);
        _host.ApplyProfile(profile);
        RefreshProfileList();
        LoadProfileIntoUi(profile);
        RefreshRouteList();
        Log($"Created profile '{profile.Name}'.");
    }

    private void OnDeleteProfileClicked(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not string name)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Delete the profile '{name}'?",
            "GhostClick dev harness",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        Log(_host.Profiles.Delete(name) ? $"Deleted '{name}'." : $"'{name}' was not there.");
        RefreshProfileList();
    }

    private string NextAvailableProfileName()
    {
        for (int i = 1; ; i++)
        {
            var candidate = "Profile " + i;
            if (!_host.ProfileStore.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private void RefreshProfileList()
    {
        var selected = _host.Profiles.Current.Name;
        ProfileBox.ItemsSource = _host.Profiles.AvailableProfiles;
        ProfileBox.SelectedItem = selected;
    }

    // ---------------------------------------------------------------- points

    private void OnRecordPointClicked(object sender, RoutedEventArgs e)
    {
        _host.Recorder.DefaultDelayMs = ParseInt(PointDelayBox, 0);
        var point = _host.Recorder.Record();
        RefreshRouteList();
        Log($"Recorded point {_host.Recorder.Count}: {point}");
    }

    private void OnRemovePointClicked(object sender, RoutedEventArgs e)
    {
        int index = RouteList.SelectedIndex;
        if (index < 0)
        {
            _host.Recorder.RemoveLast();
        }
        else
        {
            _host.Recorder.RemoveAt(index);
        }

        RefreshRouteList();
    }

    private void OnClearPointsClicked(object sender, RoutedEventArgs e)
    {
        _host.Recorder.Clear();
        RefreshRouteList();
    }

    private void RefreshRouteList()
    {
        int selected = RouteList.SelectedIndex;
        RouteList.ItemsSource = _host.Recorder.Points;
        RouteList.SelectedIndex = Math.Min(selected, _host.Recorder.Count - 1);
    }

    // ---------------------------------------------------------------- UI <-> profile

    private void PopulateChoices()
    {
        Fill(ClickModeBox, Enum.GetValues<ClickMode>());
        Fill(ActionKindBox, Enum.GetValues<ActionKind>());
        Fill(MouseButtonBox, Enum.GetValues<MouseButton>());
        Fill(ClickTypeBox, Enum.GetValues<ClickType>());
        Fill(RepeatModeBox, Enum.GetValues<RepeatMode>());
        Fill(TargetModeBox, Enum.GetValues<TargetMode>());
        Fill(MovementModeBox, Enum.GetValues<MovementMode>());
        Fill(EasingBox, Enum.GetValues<EasingMode>());
        Fill(BoundsBox, Enum.GetValues<BoundsBehavior>());

        // The action key list leaves out the mouse buttons; the hotkey lists keep them,
        // because binding a side button as a hotkey is a normal thing to want.
        var keyboardKeys = Enum.GetValues<VirtualKey>().Where(k => !k.IsMouseButton()).ToArray();
        var allKeys = Enum.GetValues<VirtualKey>();

        Fill(ActionKeyBox, keyboardKeys);
        Fill(StartStopKeyBox, allKeys);
        Fill(PauseKeyBox, allKeys);
        Fill(StopKeyBox, allKeys);
        Fill(RecordKeyBox, allKeys);
    }

    private static void Fill<T>(ComboBox box, IEnumerable<T> values) => box.ItemsSource = values.ToArray();

    private void LoadProfileIntoUi(ClickerProfile profile)
    {
        _loadingProfileIntoUi = true;

        try
        {
            NameBox.Text = profile.Name;
            ClickModeBox.SelectedItem = profile.Mode;

            ActionKindBox.SelectedItem = profile.Action.Kind;
            MouseButtonBox.SelectedItem = profile.Action.Button;
            ClickTypeBox.SelectedItem = profile.Action.ClickType;
            ActionKeyBox.SelectedItem = profile.Action.Key;
            ActionCtrlBox.IsChecked = profile.Action.KeyModifiers.HasFlag(KeyModifiers.Control);
            ActionAltBox.IsChecked = profile.Action.KeyModifiers.HasFlag(KeyModifiers.Alt);
            ActionShiftBox.IsChecked = profile.Action.KeyModifiers.HasFlag(KeyModifiers.Shift);
            HoldDurationBox.Text = Text(profile.Action.HoldDurationMs);
            DoubleGapBox.Text = Text(profile.Action.DoubleClickGapMs);

            DelayBox.Text = Text(profile.Interval.DelayMs);
            RandomIntervalBox.IsChecked = profile.Interval.RandomizeEnabled;
            MinDelayBox.Text = Text(profile.Interval.MinDelayMs);
            MaxDelayBox.Text = Text(profile.Interval.MaxDelayMs);

            RepeatModeBox.SelectedItem = profile.Repeat.Mode;
            RepeatCountBox.Text = Text(profile.Repeat.Count);

            TargetModeBox.SelectedItem = profile.Target.Mode;
            FixedXBox.Text = Text(profile.Target.FixedPoint.X);
            FixedYBox.Text = Text(profile.Target.FixedPoint.Y);
            RestoreCursorBox.IsChecked = profile.Target.RestoreCursorOnStop;
            PointDelayBox.Text = Text(_host.Recorder.DefaultDelayMs);

            MovementModeBox.SelectedItem = profile.Movement.Mode;
            EasingBox.SelectedItem = profile.Movement.Easing;
            MoveDurationBox.Text = Text(profile.Movement.DurationMs);
            MoveSpeedBox.Text = Text(profile.Movement.SpeedPixelsPerSecond);
            MoveStepBox.Text = Text(profile.Movement.StepIntervalMs);

            PositionJitterBox.IsChecked = profile.Jitter.PositionJitterEnabled;
            OffsetXBox.Text = Text(profile.Jitter.MaxOffsetX);
            OffsetYBox.Text = Text(profile.Jitter.MaxOffsetY);
            PathJitterBox.Text = Text(profile.Jitter.PathJitterPixels);
            TimingJitterBox.IsChecked = profile.Jitter.TimingJitterEnabled;
            TimingPercentBox.Text = Text(profile.Jitter.TimingJitterPercent);

            AngleBox.Text = Text(profile.Directional.AngleDegrees);
            DriftSpeedBox.Text = Text(profile.Directional.SpeedPixelsPerSecond);
            AngleJitterBox.Text = Text(profile.Directional.AngleJitterDegrees);
            BoundsBox.SelectedItem = profile.Directional.OnBounds;
            UseStartPointBox.IsChecked = profile.Directional.UseStartPoint;
            StartXBox.Text = Text(profile.Directional.StartPoint.X);
            StartYBox.Text = Text(profile.Directional.StartPoint.Y);

            LoadHotkey(profile.Hotkeys.StartStop, StartStopKeyBox, StartStopCtrlBox, StartStopAltBox, StartStopShiftBox);
            LoadHotkey(profile.Hotkeys.Pause, PauseKeyBox, PauseCtrlBox, PauseAltBox, PauseShiftBox);
            LoadHotkey(profile.Hotkeys.EmergencyStop, StopKeyBox, StopCtrlBox, StopAltBox, StopShiftBox);
            LoadHotkey(profile.Hotkeys.RecordPoint, RecordKeyBox, RecordCtrlBox, RecordAltBox, RecordShiftBox);

            FailsafeBox.IsChecked = profile.Safety.FailsafeEnabled;
            StopOnMoveBox.IsChecked = profile.Safety.StopOnUserCursorMove;
            MaxDurationBox.Text = Text(profile.Safety.MaxRunDurationSeconds);
            MaxActionsBox.Text = Text(profile.Safety.MaxActionsPerRun);
        }
        finally
        {
            _loadingProfileIntoUi = false;
        }
    }

    /// <summary>
    /// Reads the form into the current profile and hands it to the engine. Returns false and
    /// reports the problems when the result would not be a runnable profile.
    /// </summary>
    private bool TryApplyUi()
    {
        if (_loadingProfileIntoUi)
        {
            return false;
        }

        var profile = _host.Profiles.Current;

        var name = NameBox.Text.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            profile.Name = name;
        }

        profile.Mode = Selected(ClickModeBox, profile.Mode);

        profile.Action.Kind = Selected(ActionKindBox, profile.Action.Kind);
        profile.Action.Button = Selected(MouseButtonBox, profile.Action.Button);
        profile.Action.ClickType = Selected(ClickTypeBox, profile.Action.ClickType);
        profile.Action.Key = Selected(ActionKeyBox, profile.Action.Key);
        profile.Action.KeyModifiers = ReadModifiers(ActionCtrlBox, ActionAltBox, ActionShiftBox);
        profile.Action.HoldDurationMs = ParseInt(HoldDurationBox, profile.Action.HoldDurationMs);
        profile.Action.DoubleClickGapMs = ParseInt(DoubleGapBox, profile.Action.DoubleClickGapMs);

        profile.Interval.DelayMs = ParseInt(DelayBox, profile.Interval.DelayMs);
        profile.Interval.RandomizeEnabled = RandomIntervalBox.IsChecked == true;
        profile.Interval.MinDelayMs = ParseInt(MinDelayBox, profile.Interval.MinDelayMs);
        profile.Interval.MaxDelayMs = ParseInt(MaxDelayBox, profile.Interval.MaxDelayMs);

        profile.Repeat.Mode = Selected(RepeatModeBox, profile.Repeat.Mode);
        profile.Repeat.Count = ParseInt(RepeatCountBox, profile.Repeat.Count);

        profile.Target.Mode = Selected(TargetModeBox, profile.Target.Mode);
        profile.Target.FixedPoint.X = ParseInt(FixedXBox, profile.Target.FixedPoint.X);
        profile.Target.FixedPoint.Y = ParseInt(FixedYBox, profile.Target.FixedPoint.Y);
        profile.Target.RestoreCursorOnStop = RestoreCursorBox.IsChecked == true;

        profile.Movement.Mode = Selected(MovementModeBox, profile.Movement.Mode);
        profile.Movement.Easing = Selected(EasingBox, profile.Movement.Easing);
        profile.Movement.DurationMs = ParseInt(MoveDurationBox, profile.Movement.DurationMs);
        profile.Movement.SpeedPixelsPerSecond = ParseDouble(MoveSpeedBox, profile.Movement.SpeedPixelsPerSecond);
        profile.Movement.StepIntervalMs = ParseInt(MoveStepBox, profile.Movement.StepIntervalMs);

        profile.Jitter.PositionJitterEnabled = PositionJitterBox.IsChecked == true;
        profile.Jitter.MaxOffsetX = ParseInt(OffsetXBox, profile.Jitter.MaxOffsetX);
        profile.Jitter.MaxOffsetY = ParseInt(OffsetYBox, profile.Jitter.MaxOffsetY);
        profile.Jitter.PathJitterPixels = ParseInt(PathJitterBox, profile.Jitter.PathJitterPixels);
        profile.Jitter.TimingJitterEnabled = TimingJitterBox.IsChecked == true;
        profile.Jitter.TimingJitterPercent = ParseDouble(TimingPercentBox, profile.Jitter.TimingJitterPercent);

        profile.Directional.AngleDegrees = ParseDouble(AngleBox, profile.Directional.AngleDegrees);
        profile.Directional.SpeedPixelsPerSecond = ParseDouble(DriftSpeedBox, profile.Directional.SpeedPixelsPerSecond);
        profile.Directional.AngleJitterDegrees = ParseDouble(AngleJitterBox, profile.Directional.AngleJitterDegrees);
        profile.Directional.OnBounds = Selected(BoundsBox, profile.Directional.OnBounds);
        profile.Directional.UseStartPoint = UseStartPointBox.IsChecked == true;
        profile.Directional.StartPoint.X = ParseInt(StartXBox, profile.Directional.StartPoint.X);
        profile.Directional.StartPoint.Y = ParseInt(StartYBox, profile.Directional.StartPoint.Y);

        profile.Hotkeys.StartStop = ReadHotkey(StartStopKeyBox, StartStopCtrlBox, StartStopAltBox, StartStopShiftBox);
        profile.Hotkeys.Pause = ReadHotkey(PauseKeyBox, PauseCtrlBox, PauseAltBox, PauseShiftBox);
        profile.Hotkeys.EmergencyStop = ReadHotkey(StopKeyBox, StopCtrlBox, StopAltBox, StopShiftBox);
        profile.Hotkeys.RecordPoint = ReadHotkey(RecordKeyBox, RecordCtrlBox, RecordAltBox, RecordShiftBox);

        profile.Safety.FailsafeEnabled = FailsafeBox.IsChecked == true;
        profile.Safety.StopOnUserCursorMove = StopOnMoveBox.IsChecked == true;
        profile.Safety.MaxRunDurationSeconds = ParseInt(MaxDurationBox, profile.Safety.MaxRunDurationSeconds);
        profile.Safety.MaxActionsPerRun = ParseInt(MaxActionsBox, profile.Safety.MaxActionsPerRun);

        _host.Recorder.DefaultDelayMs = ParseInt(PointDelayBox, _host.Recorder.DefaultDelayMs);
        _host.Recorder.ApplyTo(profile);

        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                Log("Invalid: " + error);
            }

            return false;
        }

        // ApplyProfile reloads the recorder from the profile route, which is why the recorder
        // is copied into the profile just above rather than after.
        _host.ApplyProfile(profile);
        return true;
    }

    private static void LoadHotkey(
        HotkeyBinding binding,
        ComboBox keyBox,
        CheckBox ctrl,
        CheckBox alt,
        CheckBox shift)
    {
        keyBox.SelectedItem = binding.Key;
        ctrl.IsChecked = binding.Modifiers.HasFlag(KeyModifiers.Control);
        alt.IsChecked = binding.Modifiers.HasFlag(KeyModifiers.Alt);
        shift.IsChecked = binding.Modifiers.HasFlag(KeyModifiers.Shift);
    }

    private static HotkeyBinding ReadHotkey(ComboBox keyBox, CheckBox ctrl, CheckBox alt, CheckBox shift) =>
        new(Selected(keyBox, VirtualKey.None), ReadModifiers(ctrl, alt, shift));

    private static KeyModifiers ReadModifiers(CheckBox ctrl, CheckBox alt, CheckBox shift)
    {
        var modifiers = KeyModifiers.None;

        if (ctrl.IsChecked == true)
        {
            modifiers |= KeyModifiers.Control;
        }

        if (alt.IsChecked == true)
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (shift.IsChecked == true)
        {
            modifiers |= KeyModifiers.Shift;
        }

        return modifiers;
    }

    // ---------------------------------------------------------------- status and log

    private void UpdateStateUi(EngineState state)
    {
        StateText.Text = state.ToString();

        bool active = state is EngineState.Running or EngineState.Paused;
        StartButton.IsEnabled = !active;
        StopButton.IsEnabled = active;
        PauseButton.IsEnabled = active;
        PauseButton.Content = state == EngineState.Paused ? "Resume" : "Pause";
    }

    private void RefreshStatus()
    {
        var statistics = _host.Engine.Statistics;
        CountersText.Text =
            $"{statistics.Actions} actions | {statistics.MouseClicks} clicks | " +
            $"{statistics.KeyPresses} keys | {statistics.Runs} runs | {statistics.TotalRunTime:hh\\:mm\\:ss}";

        var cursor = _injector.GetCursorPosition();
        CursorText.Text = $"Cursor {cursor}";
    }

    private void Log(string message)
    {
        LogList.Items.Add($"{DateTime.Now:HH:mm:ss.fff}  {message}");

        while (LogList.Items.Count > MaxLogEntries)
        {
            LogList.Items.RemoveAt(0);
        }

        LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void ShowValidationErrors(ProfileValidationException exception)
    {
        foreach (var error in exception.Errors)
        {
            Log("Invalid: " + error);
        }
    }

    // ---------------------------------------------------------------- parsing helpers

    private static T Selected<T>(ComboBox box, T fallback) => box.SelectedItem is T value ? value : fallback;

    private static int ParseInt(TextBox box, int fallback) =>
        int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static double ParseDouble(TextBox box, double fallback) =>
        double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
