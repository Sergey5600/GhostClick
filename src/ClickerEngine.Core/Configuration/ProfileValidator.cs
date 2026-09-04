namespace ClickerEngine.Core.Configuration;

/// <summary>Reports a single problem found in a profile.</summary>
/// <param name="Path">Dotted property path, e.g. <c>Interval.MinDelayMs</c>.</param>
/// <param name="Message">Human readable description.</param>
public readonly record struct ValidationError(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// Checks a profile for values the engine cannot honour. The engine calls
/// <see cref="EnsureValid"/> before every run so a bad config fails loudly instead of
/// producing a silently degenerate click pattern.
/// </summary>
public static class ProfileValidator
{
    /// <summary>Returns every problem found; empty means the profile is usable.</summary>
    public static IReadOnlyList<ValidationError> Validate(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add(new ValidationError(nameof(profile.Name), "Profile name must not be empty."));
        }

        ValidateInterval(profile.Interval, errors);
        ValidateAction(profile.Action, errors);
        ValidateRepeat(profile.Repeat, errors);
        ValidateTarget(profile.Target, errors);
        ValidateMovement(profile.Movement, errors);
        ValidateJitter(profile.Jitter, errors);
        ValidateDirectional(profile, errors);
        ValidateSafety(profile.Safety, errors);

        if (profile.Mode == ClickMode.Hold && !profile.Hotkeys.StartStop.IsBound)
        {
            errors.Add(new ValidationError(
                "Hotkeys.StartStop",
                "Hold mode needs a start/stop hotkey to hold down."));
        }

        return errors;
    }

    /// <summary>Throws <see cref="ProfileValidationException"/> if the profile is unusable.</summary>
    public static void EnsureValid(ClickerProfile profile)
    {
        var errors = Validate(profile);
        if (errors.Count > 0)
        {
            throw new ProfileValidationException(errors);
        }
    }

    private static void ValidateInterval(IntervalSettings interval, List<ValidationError> errors)
    {
        if (interval.DelayMs < 0)
        {
            errors.Add(new ValidationError("Interval.DelayMs", "Delay must not be negative."));
        }

        if (!interval.RandomizeEnabled)
        {
            return;
        }

        if (interval.MinDelayMs < 0)
        {
            errors.Add(new ValidationError("Interval.MinDelayMs", "Minimum delay must not be negative."));
        }

        if (interval.MaxDelayMs < interval.MinDelayMs)
        {
            errors.Add(new ValidationError(
                "Interval.MaxDelayMs",
                $"Maximum delay ({interval.MaxDelayMs} ms) must be >= minimum delay ({interval.MinDelayMs} ms)."));
        }
    }

    private static void ValidateAction(ActionSettings action, List<ValidationError> errors)
    {
        if (action.HoldDurationMs < 0)
        {
            errors.Add(new ValidationError("Action.HoldDurationMs", "Hold duration must not be negative."));
        }

        if (action.DoubleClickGapMs < 0)
        {
            errors.Add(new ValidationError("Action.DoubleClickGapMs", "Double click gap must not be negative."));
        }

        if (action.Kind == ActionKind.Keyboard && action.Key == VirtualKey.None)
        {
            errors.Add(new ValidationError("Action.Key", "Keyboard mode needs a key to press."));
        }

        if (action.Kind == ActionKind.Keyboard && action.Key.IsMouseButton())
        {
            errors.Add(new ValidationError(
                "Action.Key",
                $"{action.Key} is a mouse button; use Action.Kind = Mouse for it."));
        }
    }

    private static void ValidateRepeat(RepeatSettings repeat, List<ValidationError> errors)
    {
        if (repeat.Mode == RepeatMode.FixedCount && repeat.Count <= 0)
        {
            errors.Add(new ValidationError("Repeat.Count", "Fixed repeat count must be at least 1."));
        }
    }

    private static void ValidateTarget(TargetSettings target, List<ValidationError> errors)
    {
        if (target.Mode != TargetMode.MacroRoute)
        {
            return;
        }

        if (!target.Route.Any(p => p.Enabled))
        {
            errors.Add(new ValidationError("Target.Route", "Macro route mode needs at least one enabled point."));
        }

        for (int i = 0; i < target.Route.Count; i++)
        {
            if (target.Route[i].DelayMs < 0)
            {
                errors.Add(new ValidationError($"Target.Route[{i}].DelayMs", "Point delay must not be negative."));
            }
        }
    }

    private static void ValidateMovement(MovementSettings movement, List<ValidationError> errors)
    {
        if (movement.Mode != MovementMode.Smooth)
        {
            return;
        }

        if (movement.DurationMs < 0)
        {
            errors.Add(new ValidationError("Movement.DurationMs", "Duration must not be negative."));
        }

        if (movement.DurationMs == 0 && movement.SpeedPixelsPerSecond <= 0)
        {
            errors.Add(new ValidationError(
                "Movement.SpeedPixelsPerSecond",
                "Smooth movement needs either a duration or a positive speed."));
        }

        if (movement.StepIntervalMs <= 0)
        {
            errors.Add(new ValidationError("Movement.StepIntervalMs", "Step interval must be at least 1 ms."));
        }

        if (movement.MaxSteps <= 0)
        {
            errors.Add(new ValidationError("Movement.MaxSteps", "Max steps must be at least 1."));
        }
    }

    private static void ValidateJitter(JitterSettings jitter, List<ValidationError> errors)
    {
        if (jitter.MaxOffsetX < 0 || jitter.MaxOffsetY < 0)
        {
            errors.Add(new ValidationError("Jitter.MaxOffset", "Jitter offsets must not be negative."));
        }

        if (jitter.PathJitterPixels < 0)
        {
            errors.Add(new ValidationError("Jitter.PathJitterPixels", "Path jitter must not be negative."));
        }

        if (jitter.TimingJitterPercent is < 0 or > 100)
        {
            errors.Add(new ValidationError("Jitter.TimingJitterPercent", "Timing jitter must be between 0 and 100 percent."));
        }
    }

    private static void ValidateDirectional(ClickerProfile profile, List<ValidationError> errors)
    {
        if (profile.Target.Mode != TargetMode.Directional)
        {
            return;
        }

        var directional = profile.Directional;
        if (directional.SpeedPixelsPerSecond <= 0)
        {
            errors.Add(new ValidationError("Directional.SpeedPixelsPerSecond", "Drift speed must be positive."));
        }

        if (directional.StepIntervalMs <= 0)
        {
            errors.Add(new ValidationError("Directional.StepIntervalMs", "Drift step interval must be at least 1 ms."));
        }

        if (directional.AngleJitterDegrees < 0)
        {
            errors.Add(new ValidationError("Directional.AngleJitterDegrees", "Angle jitter must not be negative."));
        }
    }

    private static void ValidateSafety(SafetySettings safety, List<ValidationError> errors)
    {
        if (safety.MaxRunDurationSeconds < 0)
        {
            errors.Add(new ValidationError("Safety.MaxRunDurationSeconds", "Run duration limit must not be negative."));
        }

        if (safety.MaxActionsPerRun < 0)
        {
            errors.Add(new ValidationError("Safety.MaxActionsPerRun", "Action limit must not be negative."));
        }

        if (safety.StopOnUserCursorMove && safety.UserCursorMoveThresholdPixels <= 0)
        {
            errors.Add(new ValidationError(
                "Safety.UserCursorMoveThresholdPixels",
                "Cursor move threshold must be positive when the check is enabled."));
        }
    }
}

/// <summary>Thrown when a profile fails validation.</summary>
public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(IReadOnlyList<ValidationError> errors)
        : base("Profile is not valid: " + string.Join("; ", errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<ValidationError> Errors { get; }
}
