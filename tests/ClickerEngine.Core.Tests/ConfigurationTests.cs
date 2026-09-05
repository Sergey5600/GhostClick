using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using Xunit;

namespace ClickerEngine.Core.Tests;

public class ConfigurationTests
{
    [Fact]
    public void DefaultProfile_IsValidAndMatchesTheStageOneSpec()
    {
        var profile = ClickerProfile.CreateDefault();

        Assert.Empty(ProfileValidator.Validate(profile));
        Assert.Equal(ClickMode.Toggle, profile.Mode);
        Assert.Equal(ActionKind.Mouse, profile.Action.Kind);
        Assert.Equal(MouseButton.Left, profile.Action.Button);
        Assert.Equal(TargetMode.CurrentCursor, profile.Target.Mode);
        Assert.Equal(VirtualKey.F6, profile.Hotkeys.StartStop.Key);
        Assert.Equal(VirtualKey.Escape, profile.Hotkeys.EmergencyStop.Key);
        Assert.True(profile.Safety.FailsafeEnabled);
    }

    [Fact]
    public void Validator_RejectsANegativeInterval()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Interval.DelayMs = -1;

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Interval.DelayMs");
    }

    [Fact]
    public void Validator_RejectsAnInvertedRandomRange()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Interval.RandomizeEnabled = true;
        profile.Interval.MinDelayMs = 200;
        profile.Interval.MaxDelayMs = 100;

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Interval.MaxDelayMs");
    }

    [Fact]
    public void Validator_IgnoresTheRandomRangeWhenRandomizationIsOff()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Interval.RandomizeEnabled = false;
        profile.Interval.MinDelayMs = 200;
        profile.Interval.MaxDelayMs = 100;

        Assert.Empty(ProfileValidator.Validate(profile));
    }

    [Fact]
    public void Validator_RequiresAtLeastOneEnabledPointForAMacroRoute()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Target.Mode = TargetMode.MacroRoute;
        profile.Target.Route = [new ClickPoint(1, 1) { Enabled = false }];

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Target.Route");
    }

    [Fact]
    public void Validator_RequiresAKeyInKeyboardMode()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Action.Kind = ActionKind.Keyboard;
        profile.Action.Key = VirtualKey.None;

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Action.Key");
    }

    [Fact]
    public void Validator_RejectsAMouseButtonUsedAsAKeyboardKey()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Action.Kind = ActionKind.Keyboard;
        profile.Action.Key = VirtualKey.LeftButton;

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Action.Key");
    }

    [Fact]
    public void Validator_RequiresADurationOrASpeedForSmoothMovement()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Movement.Mode = MovementMode.Smooth;
        profile.Movement.DurationMs = 0;
        profile.Movement.SpeedPixelsPerSecond = 0;

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Movement.SpeedPixelsPerSecond");
    }

    [Fact]
    public void Validator_ChecksDirectionalSettingsOnlyInDirectionalMode()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Directional.SpeedPixelsPerSecond = 0;

        Assert.Empty(ProfileValidator.Validate(profile));

        profile.Target.Mode = TargetMode.Directional;
        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Directional.SpeedPixelsPerSecond");
    }

    [Fact]
    public void Validator_RequiresAStartStopBindingForHoldMode()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Mode = ClickMode.Hold;
        profile.Hotkeys.StartStop = new HotkeyBinding();

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Hotkeys.StartStop");
    }

    [Fact]
    public void Validator_RejectsTimingJitterOutsideZeroToOneHundredPercent()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Jitter.TimingJitterPercent = 140;

        Assert.Contains(ProfileValidator.Validate(profile), e => e.Path == "Jitter.TimingJitterPercent");
    }

    [Fact]
    public void EnsureValid_ThrowsWithEveryProblemListed()
    {
        var profile = ClickerProfile.CreateDefault();
        profile.Name = " ";
        profile.Interval.DelayMs = -1;

        var error = Assert.Throws<ProfileValidationException>(() => ProfileValidator.EnsureValid(profile));

        Assert.Equal(2, error.Errors.Count);
        Assert.Contains("Interval.DelayMs", error.Message);
    }

    [Fact]
    public void ScreenRect_ClampsToTheLastAddressablePixel()
    {
        var bounds = new ScreenRect(0, 0, 1920, 1080);

        Assert.Equal(new ScreenPoint(1919, 1079), bounds.Clamp(new ScreenPoint(5000, 5000)));
        Assert.Equal(new ScreenPoint(0, 0), bounds.Clamp(new ScreenPoint(-10, -10)));
        Assert.Equal(new ScreenPoint(50, 60), bounds.Clamp(new ScreenPoint(50, 60)));
    }

    [Fact]
    public void ScreenRect_HandlesAMultiMonitorDesktopWithNegativeOrigin()
    {
        // A second monitor to the left of the primary gives the virtual desktop a negative
        // left edge; coordinates there are valid and must survive clamping.
        var bounds = new ScreenRect(-1920, 0, 3840, 1080);

        Assert.True(bounds.Contains(new ScreenPoint(-1000, 500)));
        Assert.Equal(new ScreenPoint(-1920, 0), bounds.Clamp(new ScreenPoint(-9999, -1)));
        Assert.Equal(new ScreenPoint(1919, 1079), bounds.Clamp(new ScreenPoint(9999, 9999)));
    }

    [Fact]
    public void ScreenPoint_MeasuresDistance()
    {
        Assert.Equal(5.0, new ScreenPoint(0, 0).DistanceTo(new ScreenPoint(3, 4)), 6);
        Assert.Equal(0.0, new ScreenPoint(7, 7).DistanceTo(new ScreenPoint(7, 7)), 6);
    }

    [Fact]
    public void AbsoluteCoordinates_MapTheDesktopEdgesOntoTheFullRange()
    {
        var bounds = new ScreenRect(0, 0, 1920, 1080);

        Assert.Equal((0, 0), AbsoluteCoordinates.Normalize(new ScreenPoint(0, 0), bounds));
        Assert.Equal(
            (AbsoluteCoordinates.Max, AbsoluteCoordinates.Max),
            AbsoluteCoordinates.Normalize(new ScreenPoint(1919, 1079), bounds));

        var (midX, midY) = AbsoluteCoordinates.Normalize(new ScreenPoint(960, 540), bounds);
        Assert.InRange(midX, 32000, 33500);
        Assert.InRange(midY, 32000, 33500);
    }

    [Fact]
    public void AbsoluteCoordinates_AreRelativeToTheVirtualDesktopOrigin()
    {
        var bounds = new ScreenRect(-1920, 0, 3840, 1080);

        Assert.Equal(0, AbsoluteCoordinates.Normalize(new ScreenPoint(-1920, 0), bounds).X);
        Assert.Equal(AbsoluteCoordinates.Max, AbsoluteCoordinates.Normalize(new ScreenPoint(1919, 0), bounds).X);
    }

    [Fact]
    public void AbsoluteCoordinates_ClampOutOfRangeInput()
    {
        var bounds = new ScreenRect(0, 0, 1920, 1080);

        Assert.Equal((0, 0), AbsoluteCoordinates.Normalize(new ScreenPoint(-500, -500), bounds));
        Assert.Equal(
            (AbsoluteCoordinates.Max, AbsoluteCoordinates.Max),
            AbsoluteCoordinates.Normalize(new ScreenPoint(99999, 99999), bounds));
    }

    [Fact]
    public void AbsoluteCoordinates_SurviveADegenerateOnePixelDesktop()
    {
        var bounds = new ScreenRect(0, 0, 1, 1);

        Assert.Equal((0, 0), AbsoluteCoordinates.Normalize(new ScreenPoint(0, 0), bounds));
    }

    [Fact]
    public void RecordingInjector_TracksTheCursorItIsToldToMove()
    {
        var injector = new RecordingInputInjector(new ScreenPoint(10, 10));

        injector.MoveTo(new ScreenPoint(40, 50));
        injector.MouseButtonDown(MouseButton.Right);
        injector.MouseButtonUp(MouseButton.Right);

        Assert.Equal(new ScreenPoint(40, 50), injector.GetCursorPosition());
        Assert.Collection(
            injector.Events,
            e => Assert.Equal(InjectedEventKind.Move, e.Kind),
            e =>
            {
                Assert.Equal(InjectedEventKind.MouseDown, e.Kind);
                Assert.Equal(new ScreenPoint(40, 50), e.Position);
            },
            e => Assert.Equal(InjectedEventKind.MouseUp, e.Kind));
    }
}
