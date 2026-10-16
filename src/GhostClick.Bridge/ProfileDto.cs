using ClickerEngine.Core.Configuration;

namespace GhostClick.Bridge;

/// <summary>
/// The slice of a profile the web interface edits, in the page's own vocabulary. The page
/// holds this object, changes it, and sends the whole thing back; <see cref="ProfileMapper"/>
/// writes it onto the real profile, leaving every setting the page does not show untouched.
/// </summary>
public sealed class ProfileDto
{
    public BindingsDto Bind { get; set; } = new();

    /// <summary>Everything pressed together on each tick.</summary>
    public List<ChordDto> Actions { get; set; } = new();

    public List<PointDto> Points { get; set; } = new();

    /// <summary><c>toggle</c> or <c>hold</c>.</summary>
    public string Mode { get; set; } = "toggle";

    /// <summary>
    /// <c>cursor</c> or <c>points</c>; <c>fixed</c> and <c>directional</c> pass through for
    /// profiles set up elsewhere.
    /// </summary>
    public string Target { get; set; } = "cursor";

    /// <summary><c>single</c> or <c>double</c>.</summary>
    public string Click { get; set; } = "single";

    /// <summary><c>instant</c> or <c>smooth</c>.</summary>
    public string Move { get; set; } = "instant";

    /// <summary><c>inf</c> or <c>fixed</c>.</summary>
    public string Repeat { get; set; } = "inf";

    public int Count { get; set; } = 100;

    public int Delay { get; set; } = 100;

    /// <summary>Random interval between <see cref="MinDelay"/> and <see cref="MaxDelay"/>.</summary>
    public bool Rand { get; set; }

    public int MinDelay { get; set; } = 80;

    public int MaxDelay { get; set; } = 140;

    public FlagsDto Flags { get; set; } = new();

    /// <summary>Largest position jitter in pixels; shown in a label, never written back.</summary>
    public int JitterPx { get; set; } = 3;
}

/// <summary>The four hotkey slots; null means unassigned.</summary>
public sealed class BindingsDto
{
    public ChordDto? Toggle { get; set; }

    public ChordDto? Pause { get; set; }

    public ChordDto? Panic { get; set; }

    public ChordDto? Point { get; set; }
}

/// <summary>A key or mouse button (<see cref="KeyCodes"/> names) with modifier names.</summary>
public sealed class ChordDto
{
    public ChordDto()
    {
    }

    public ChordDto(string key, params string[] mods)
    {
        Key = key;
        Mods = mods.ToList();
    }

    public List<string> Mods { get; set; } = new();

    public string Key { get; set; } = string.Empty;
}

/// <summary>A route point; the fields the page does not show ride along unchanged.</summary>
public sealed class PointDto
{
    public int X { get; set; }

    public int Y { get; set; }

    public int DelayMs { get; set; }

    public string? Label { get; set; }

    public bool Enabled { get; set; } = true;
}

/// <summary>The toggles under «Безопасность».</summary>
public sealed class FlagsDto
{
    /// <summary>Position jitter around the click point.</summary>
    public bool Jitter { get; set; }

    /// <summary>Random hold duration per press.</summary>
    public bool HoldRandom { get; set; }

    /// <summary>Stop when the user moves the mouse.</summary>
    public bool StopOnMouseMove { get; set; }

    /// <summary>Keep input in the window the run started on.</summary>
    public bool ActiveWindowOnly { get; set; }
}

/// <summary>Converts between <see cref="ClickerProfile"/> and <see cref="ProfileDto"/>.</summary>
public static class ProfileMapper
{
    public static ProfileDto ToDto(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new ProfileDto
        {
            Bind = new BindingsDto
            {
                Toggle = ToDto(profile.Hotkeys.StartStop),
                Pause = ToDto(profile.Hotkeys.Pause),
                Panic = ToDto(profile.Hotkeys.EmergencyStop),
                Point = ToDto(profile.Hotkeys.RecordPoint),
            },
            Actions = (profile.Action.Inputs ?? new())
                .Where(i => i is not null && i.Key != VirtualKey.None)
                .Select(i => ToDto(i.Key, i.Modifiers))
                .ToList(),
            Points = (profile.Target.Route ?? new())
                .Select(p => new PointDto { X = p.X, Y = p.Y, DelayMs = p.DelayMs, Label = p.Label, Enabled = p.Enabled })
                .ToList(),
            Mode = profile.Mode == ClickMode.Hold ? "hold" : "toggle",
            Target = profile.Target.Mode switch
            {
                TargetMode.MacroRoute => "points",
                TargetMode.FixedPoint => "fixed",
                TargetMode.Directional => "directional",
                _ => "cursor",
            },
            Click = profile.Action.ClickType == ClickType.Double ? "double" : "single",
            Move = profile.Movement.Mode == MovementMode.Smooth ? "smooth" : "instant",
            Repeat = profile.Repeat.Mode == RepeatMode.FixedCount ? "fixed" : "inf",
            Count = profile.Repeat.Count,
            Delay = profile.Interval.DelayMs,
            Rand = profile.Interval.RandomizeEnabled,
            MinDelay = profile.Interval.MinDelayMs,
            MaxDelay = profile.Interval.MaxDelayMs,
            Flags = new FlagsDto
            {
                Jitter = profile.Jitter.PositionJitterEnabled,
                HoldRandom = profile.Action.RandomizeHoldDuration,
                StopOnMouseMove = profile.Safety.StopOnUserCursorMove,
                ActiveWindowOnly = profile.Safety.ActiveWindowOnly,
            },
            JitterPx = Math.Max(profile.Jitter.MaxOffsetX, profile.Jitter.MaxOffsetY),
        };
    }

    /// <summary>
    /// Writes the page's edits onto <paramref name="profile"/>. Values the page could not
    /// have meant (an unknown mode name, an unknown key) leave the existing setting alone.
    /// </summary>
    public static void Apply(ProfileDto dto, ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(profile);

        if (dto.Bind is { } bind)
        {
            profile.Hotkeys.StartStop = ToBinding(bind.Toggle, profile.Hotkeys.StartStop);
            profile.Hotkeys.Pause = ToBinding(bind.Pause, profile.Hotkeys.Pause);
            profile.Hotkeys.EmergencyStop = ToBinding(bind.Panic, profile.Hotkeys.EmergencyStop);
            profile.Hotkeys.RecordPoint = ToBinding(bind.Point, profile.Hotkeys.RecordPoint);
        }

        if (dto.Actions is { } actions)
        {
            var inputs = new List<InputChord>();
            foreach (var chord in actions)
            {
                if (chord is not null && KeyCodes.TryGetVirtualKey(chord.Key, out var key))
                {
                    inputs.Add(new InputChord(key, KeyCodes.ParseModifiers(chord.Mods)));
                }
            }

            profile.Action.Inputs = inputs;
        }

        if (dto.Points is { } points)
        {
            profile.Target.Route = points
                .Where(p => p is not null)
                .Select(p => new ClickPoint(p.X, p.Y, Math.Max(0, p.DelayMs), p.Label) { Enabled = p.Enabled })
                .ToList();
        }

        profile.Mode = dto.Mode switch
        {
            "toggle" => ClickMode.Toggle,
            "hold" => ClickMode.Hold,
            _ => profile.Mode,
        };

        profile.Target.Mode = dto.Target switch
        {
            "cursor" => TargetMode.CurrentCursor,
            "points" => TargetMode.MacroRoute,
            "fixed" => TargetMode.FixedPoint,
            "directional" => TargetMode.Directional,
            _ => profile.Target.Mode,
        };

        profile.Action.ClickType = dto.Click switch
        {
            "single" => ClickType.Single,
            "double" => ClickType.Double,
            _ => profile.Action.ClickType,
        };

        profile.Movement.Mode = dto.Move switch
        {
            "instant" => MovementMode.Instant,
            "smooth" => MovementMode.Smooth,
            _ => profile.Movement.Mode,
        };

        profile.Repeat.Mode = dto.Repeat switch
        {
            "inf" => RepeatMode.Infinite,
            "fixed" => RepeatMode.FixedCount,
            _ => profile.Repeat.Mode,
        };

        profile.Repeat.Count = dto.Count;
        profile.Interval.DelayMs = dto.Delay;
        profile.Interval.RandomizeEnabled = dto.Rand;
        profile.Interval.MinDelayMs = dto.MinDelay;
        profile.Interval.MaxDelayMs = dto.MaxDelay;

        if (dto.Flags is { } flags)
        {
            profile.Jitter.PositionJitterEnabled = flags.Jitter;
            profile.Action.RandomizeHoldDuration = flags.HoldRandom;
            profile.Safety.StopOnUserCursorMove = flags.StopOnMouseMove;
            profile.Safety.ActiveWindowOnly = flags.ActiveWindowOnly;
        }

        ApplyInfiniteRepeat(profile);
    }

    /// <summary>
    /// «Повтор: Бесконечно» promises a run that only the user ends. The engine's run time
    /// limit (<see cref="SafetySettings.MaxRunDurationSeconds"/>, ten minutes by default) is a
    /// safety net the interface never shows, and it would quietly end such a run, so an
    /// infinite repeat lifts it. A fixed count keeps whatever limit the profile has.
    /// </summary>
    public static void ApplyInfiniteRepeat(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Repeat.Mode == RepeatMode.Infinite)
        {
            profile.Safety.MaxRunDurationSeconds = 0;
        }
    }

    private static ChordDto? ToDto(HotkeyBinding binding) =>
        binding is { IsBound: true } ? ToDto(binding.Key, binding.Modifiers) : null;

    private static ChordDto ToDto(VirtualKey key, KeyModifiers modifiers) =>
        new(KeyCodes.ToCode(key), KeyCodes.FormatModifiers(modifiers));

    private static HotkeyBinding ToBinding(ChordDto? chord, HotkeyBinding current)
    {
        if (chord is null)
        {
            return new HotkeyBinding();
        }

        return KeyCodes.TryGetVirtualKey(chord.Key, out var key)
            ? new HotkeyBinding(key, KeyCodes.ParseModifiers(chord.Mods))
            : current;
    }
}
