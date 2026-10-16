using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Profiles;
using Xunit;

namespace GhostClick.Bridge.Tests;

public class MappingTests
{
    /// <summary>Every key drawn on the interface's keyboard and mouse.</summary>
    private static readonly string[] PageKeyCodes =
    [
        "Mouse1", "Mouse2", "Mouse3", "Mouse4", "Mouse5",
        "Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "Backquote", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0",
        "Minus", "Equal", "Backspace", "Tab",
        "KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI", "KeyO", "KeyP", "BracketLeft", "BracketRight", "Backslash",
        "CapsLock", "KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK", "KeyL", "Semicolon", "Quote", "Enter",
        "ShiftLeft", "KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM", "Comma", "Period", "Slash", "ShiftRight",
        "ControlLeft", "MetaLeft", "AltLeft", "Space", "AltRight", "MetaRight", "ContextMenu", "ControlRight",
        "PrintScreen", "ScrollLock", "Pause", "Insert", "Home", "PageUp", "Delete", "End", "PageDown",
        "ArrowUp", "ArrowLeft", "ArrowDown", "ArrowRight",
    ];

    public static readonly TheoryData<string> PageKeys = new(PageKeyCodes);

    [Theory]
    [MemberData(nameof(PageKeys))]
    public void EveryPageKeyMapsToAVirtualKeyAndBack(string code)
    {
        Assert.True(KeyCodes.TryGetVirtualKey(code, out var key), code);
        Assert.NotEqual(VirtualKey.None, key);
        Assert.Equal(code, KeyCodes.ToCode(key));
    }

    [Fact]
    public void PageKeysAreDistinctVirtualKeys()
    {
        var keys = PageKeyCodes.Select(code =>
        {
            KeyCodes.TryGetVirtualKey(code, out var key);
            return key;
        });

        Assert.Equal(PageKeyCodes.Length, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("Mouse1", VirtualKey.LeftButton)]
    [InlineData("Mouse4", VirtualKey.XButton1)]
    [InlineData("Mouse5", VirtualKey.XButton2)]
    [InlineData("KeyQ", VirtualKey.Q)]
    [InlineData("Digit0", VirtualKey.D0)]
    [InlineData("BracketLeft", VirtualKey.Oem4)]
    [InlineData("Enter", VirtualKey.Enter)]
    [InlineData("NumpadEnter", VirtualKey.Enter)]
    [InlineData("ArrowLeft", VirtualKey.Left)]
    public void KnownCodesMapToTheMatchingVirtualKey(string code, VirtualKey expected)
    {
        Assert.True(KeyCodes.TryGetVirtualKey(code, out var key));
        Assert.Equal(expected, key);
    }

    [Fact]
    public void KeysWithoutAWebNameRoundTripThroughTheRawForm()
    {
        Assert.Equal("VK_B3", KeyCodes.ToCode(VirtualKey.MediaPlayPause));
        Assert.True(KeyCodes.TryGetVirtualKey("VK_B3", out var key));
        Assert.Equal(VirtualKey.MediaPlayPause, key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("KeyÖ")]
    [InlineData("VK_")]
    [InlineData("VK_ZZ")]
    [InlineData("VK_00")]
    public void UnknownCodesAreRejected(string? code)
    {
        Assert.False(KeyCodes.TryGetVirtualKey(code, out _));
    }

    [Fact]
    public void ModifiersParseAndFormatInThePagesOrder()
    {
        var modifiers = KeyCodes.ParseModifiers(["Win", "Ctrl", "Nonsense", "Shift"]);

        Assert.Equal(KeyModifiers.Windows | KeyModifiers.Control | KeyModifiers.Shift, modifiers);
        Assert.Equal(new[] { "Ctrl", "Shift", "Win" }, KeyCodes.FormatModifiers(modifiers));
    }

    [Fact]
    public void DescribeWritesChordsTheWayThePageDoes()
    {
        Assert.Equal("Ctrl + F12", KeyCodes.Describe(VirtualKey.F12, KeyModifiers.Control));
        Assert.Equal("ЛКМ", KeyCodes.Describe(VirtualKey.LeftButton, KeyModifiers.None));
        Assert.Equal("Shift + Alt + PgUp", KeyCodes.Describe(VirtualKey.PageUp, KeyModifiers.Alt | KeyModifiers.Shift));
    }

    [Fact]
    public void Dto_ReflectsTheProfile()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Action.Inputs = [InputChord.Mouse(MouseButton.Left), new InputChord(VirtualKey.E, KeyModifiers.Control)];
        profile.Hotkeys.Pause = new HotkeyBinding();
        profile.Target.Mode = TargetMode.MacroRoute;
        profile.Target.Route = [new ClickPoint(10, 20, 30, "door") { Enabled = false }];
        profile.Jitter.MaxOffsetY = 7;

        var dto = ProfileMapper.ToDto(profile);

        Assert.Equal("F6", dto.Bind.Toggle!.Key);
        Assert.Null(dto.Bind.Pause);
        Assert.Equal(new[] { "Ctrl" }, dto.Bind.Panic!.Mods);
        Assert.Equal("F12", dto.Bind.Panic.Key);
        Assert.Equal("F9", dto.Bind.Point!.Key);
        Assert.Equal(new[] { "Mouse1", "KeyE" }, dto.Actions.Select(a => a.Key));
        Assert.Equal(new[] { "Ctrl" }, dto.Actions[1].Mods);
        Assert.Equal("points", dto.Target);
        Assert.Equal("door", Assert.Single(dto.Points).Label);
        Assert.False(dto.Points[0].Enabled);
        Assert.Equal(7, dto.JitterPx);
    }

    [Fact]
    public void Apply_OfAnUnchangedDtoLeavesTheProfileIdentical()
    {
        var profile = UiDefaults.CreateProfile("Farm");
        profile.Mode = ClickMode.Hold;
        profile.Action.Inputs = [InputChord.Mouse(MouseButton.XButton2, KeyModifiers.Alt), new InputChord(VirtualKey.MediaPlayPause)];
        profile.Action.ClickType = ClickType.Double;
        profile.Target.Mode = TargetMode.Directional;
        profile.Target.Route = [new ClickPoint(1, 2, 3, "a"), new ClickPoint(4, 5) { Enabled = false }];
        profile.Interval.RandomizeEnabled = true;
        profile.Repeat.Mode = RepeatMode.FixedCount;
        profile.Repeat.Count = 77;
        profile.Movement.Mode = MovementMode.Smooth;
        profile.Hotkeys.RecordPoint = new HotkeyBinding();
        profile.Safety.StopOnUserCursorMove = true;

        var before = ProfileSerializer.Serialize(profile);
        ProfileMapper.Apply(ProfileMapper.ToDto(profile), profile);

        // Otherwise the interface would report unsaved changes the moment it echoed a profile back.
        Assert.Equal(before, ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void Apply_WritesEveryFieldThePageEdits()
    {
        var profile = ClickerProfile.CreateDefault();
        var dto = new ProfileDto
        {
            Bind = new BindingsDto
            {
                Toggle = new ChordDto("Mouse5"),
                Pause = null,
                Panic = new ChordDto("Escape", "Ctrl", "Shift"),
                Point = new ChordDto("KeyP"),
            },
            Actions = [new ChordDto("Mouse2"), new ChordDto("Space", "Alt")],
            Points = [new PointDto { X = 5, Y = 6 }],
            Mode = "hold",
            Target = "points",
            Click = "double",
            Move = "smooth",
            Repeat = "fixed",
            Count = 12,
            Delay = 250,
            Rand = true,
            MinDelay = 40,
            MaxDelay = 60,
            Flags = new FlagsDto { Jitter = true, HoldRandom = true, StopOnMouseMove = true, ActiveWindowOnly = true },
        };

        ProfileMapper.Apply(dto, profile);

        Assert.Equal(new HotkeyBinding(VirtualKey.XButton2), profile.Hotkeys.StartStop);
        Assert.False(profile.Hotkeys.Pause.IsBound);
        Assert.Equal(new HotkeyBinding(VirtualKey.Escape, KeyModifiers.Control | KeyModifiers.Shift), profile.Hotkeys.EmergencyStop);
        Assert.Equal(VirtualKey.P, profile.Hotkeys.RecordPoint.Key);
        Assert.Equal(
            new[] { InputChord.Mouse(MouseButton.Right), new InputChord(VirtualKey.Space, KeyModifiers.Alt) },
            profile.Action.Inputs);
        Assert.Equal(new ScreenPoint(5, 6), Assert.Single(profile.Target.Route).ToScreenPoint());
        Assert.Equal(ClickMode.Hold, profile.Mode);
        Assert.Equal(TargetMode.MacroRoute, profile.Target.Mode);
        Assert.Equal(ClickType.Double, profile.Action.ClickType);
        Assert.Equal(MovementMode.Smooth, profile.Movement.Mode);
        Assert.Equal(RepeatMode.FixedCount, profile.Repeat.Mode);
        Assert.Equal(12, profile.Repeat.Count);
        Assert.Equal(250, profile.Interval.DelayMs);
        Assert.True(profile.Interval.RandomizeEnabled);
        Assert.Equal(40, profile.Interval.MinDelayMs);
        Assert.Equal(60, profile.Interval.MaxDelayMs);
        Assert.True(profile.Jitter.PositionJitterEnabled);
        Assert.True(profile.Action.RandomizeHoldDuration);
        Assert.True(profile.Safety.StopOnUserCursorMove);
        Assert.True(profile.Safety.ActiveWindowOnly);
    }

    [Fact]
    public void Apply_KeepsSettingsWhenThePageSendsSomethingItCannotHaveMeant()
    {
        var profile = ClickerProfile.CreateDefault();
        var dto = ProfileMapper.ToDto(profile);
        dto.Mode = "sideways";
        dto.Target = "moon";
        dto.Bind.Toggle = new ChordDto("NotAKey");
        dto.Actions = [new ChordDto("NotAKey"), new ChordDto("KeyE")];

        ProfileMapper.Apply(dto, profile);

        Assert.Equal(ClickMode.Toggle, profile.Mode);
        Assert.Equal(TargetMode.CurrentCursor, profile.Target.Mode);
        Assert.Equal(VirtualKey.F6, profile.Hotkeys.StartStop.Key);
        Assert.Equal(new InputChord(VirtualKey.E), Assert.Single(profile.Action.Inputs));
    }

    [Fact]
    public void UiDefaults_TurnOnTheSafetyTogglesTheDesignStartsWith()
    {
        var profile = UiDefaults.CreateProfile("Fishing");

        Assert.Equal("Fishing", profile.Name);
        Assert.True(profile.Jitter.PositionJitterEnabled);
        Assert.True(profile.Action.RandomizeHoldDuration);
        Assert.True(profile.Safety.ActiveWindowOnly);
        Assert.False(profile.Safety.StopOnUserCursorMove);
        Assert.Empty(ProfileValidator.Validate(profile));
    }
}
