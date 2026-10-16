using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;

namespace ClickerEngine.Core.Planning;

/// <summary>
/// Turns a <see cref="ClickerProfile"/> into a lazy stream of <see cref="EngineStep"/>.
/// <para>
/// The stream is produced on demand, so every cursor read happens at the moment the engine
/// is about to need it - which is what makes "click wherever the cursor is right now" work
/// while still being a pure, side-effect-free description of the run.
/// </para>
/// <para>
/// An infinite run yields an infinite sequence; the engine is what stops enumerating.
/// </para>
/// </summary>
public sealed class ActionPlanner
{
    private readonly ClickerProfile _profile;
    private readonly ICursorProvider _cursor;
    private readonly IScreenGeometry _screen;
    private readonly IRandomSource _random;

    public ActionPlanner(
        ClickerProfile profile,
        ICursorProvider cursor,
        IScreenGeometry screen,
        IRandomSource? random = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        _random = random ?? NeutralRandomSource.Instance;
    }

    /// <summary>The full plan for one run, according to the profile's target mode.</summary>
    public IEnumerable<EngineStep> CreatePlan() => _profile.Target.Mode switch
    {
        TargetMode.CurrentCursor => PlanAtCursor(),
        TargetMode.FixedPoint => PlanFixedPoint(),
        TargetMode.MacroRoute => PlanMacroRoute(),
        TargetMode.Directional => PlanDirectional(),
        _ => throw new NotSupportedException($"Unknown target mode '{_profile.Target.Mode}'."),
    };

    /// <summary>
    /// The number of actions a run will perform, or <see langword="null"/> when it is
    /// unbounded. Used by the UI for a progress readout.
    /// </summary>
    public long? PlannedActionCount => _profile.Repeat.Mode == RepeatMode.FixedCount
        ? Math.Max(1, _profile.Repeat.Count)
        : null;

    private IEnumerable<EngineStep> PlanAtCursor()
    {
        long index = 0;
        ScreenPoint anchor = default;
        ScreenPoint? lastJittered = null;

        foreach (var _ in Iterations())
        {
            if (index > 0)
            {
                yield return EngineStep.Wait(NextInterval());
            }

            // "Click where the cursor is" means no movement at all - unless position jitter
            // is on, in which case we nudge around the current spot on purpose.
            if (_profile.Jitter.PositionJitterEnabled)
            {
                // Jitter around the spot the user aimed at, not around our own previous
                // nudge: offsets taken from the last jittered position would add up into a
                // random walk that slowly carries the cursor away. A cursor that is not where
                // we left it has been moved by the user, which re-aims.
                var cursor = _cursor.GetCursorPosition();
                if (lastJittered != cursor)
                {
                    anchor = cursor;
                }

                var jittered = Clamp(Jitter.ApplyPosition(anchor, _profile.Jitter, _random));
                lastJittered = jittered;
                yield return EngineStep.MoveTo(jittered);
            }

            foreach (var step in EmitAction())
            {
                yield return step;
            }

            index++;
        }
    }

    private IEnumerable<EngineStep> PlanFixedPoint()
    {
        long index = 0;
        foreach (var _ in Iterations())
        {
            if (index > 0)
            {
                yield return EngineStep.Wait(NextInterval());
            }

            var target = Clamp(Jitter.ApplyPosition(
                _profile.Target.FixedPoint.ToScreenPoint(),
                _profile.Jitter,
                _random));

            foreach (var step in MoveSteps(target))
            {
                yield return step;
            }

            foreach (var step in EmitAction())
            {
                yield return step;
            }

            index++;
        }
    }

    private IEnumerable<EngineStep> PlanMacroRoute()
    {
        // Snapshot the route so editing it mid-run cannot corrupt the walk.
        var points = _profile.Target.Route.Where(p => p.Enabled).ToArray();
        if (points.Length == 0)
        {
            yield break;
        }

        long index = 0;
        var pendingDelay = TimeSpan.Zero;

        foreach (var _ in Iterations())
        {
            if (index > 0)
            {
                yield return EngineStep.Wait(pendingDelay);
            }

            var point = points[(int)(index % points.Length)];
            var target = Clamp(Jitter.ApplyPosition(point.ToScreenPoint(), _profile.Jitter, _random));

            foreach (var step in MoveSteps(target))
            {
                yield return step;
            }

            foreach (var step in EmitAction())
            {
                yield return step;
            }

            // A point with its own dwell time overrides the profile-wide interval.
            pendingDelay = point.DelayMs > 0
                ? Jitter.ApplyTiming(TimeSpan.FromMilliseconds(point.DelayMs), _profile.Jitter, _random)
                : NextInterval();

            index++;
        }
    }

    private IEnumerable<EngineStep> PlanDirectional()
    {
        var settings = _profile.Directional;
        var bounds = _screen.VirtualBounds;

        var start = settings.UseStartPoint
            ? Clamp(settings.StartPoint.ToScreenPoint())
            : Clamp(_cursor.GetCursorPosition());

        if (settings.UseStartPoint)
        {
            foreach (var step in MoveSteps(start))
            {
                yield return step;
            }
        }

        var walker = new DirectionalWalker(start, settings, bounds, _random);

        long index = 0;
        foreach (var _ in Iterations())
        {
            if (index > 0)
            {
                // The drift itself consumes the interval: the cursor keeps travelling
                // between clicks instead of standing still and then teleporting.
                walker.PerturbAngle();

                foreach (var sample in walker.Advance(NextInterval()))
                {
                    if (sample.Delay > TimeSpan.Zero)
                    {
                        yield return EngineStep.Wait(sample.Delay);
                    }

                    yield return EngineStep.MoveTo(sample.Position);
                }

                if (walker.IsStopped)
                {
                    yield break;
                }
            }

            foreach (var step in EmitAction())
            {
                yield return step;
            }

            index++;
        }
    }

    /// <summary>
    /// The press/release steps for one logical action. Every input goes down in list order,
    /// they are held together, then released in reverse order; the modifiers of all inputs
    /// wrap the whole thing, double click included.
    /// </summary>
    private IEnumerable<EngineStep> EmitAction()
    {
        var action = _profile.Action;
        int repeats = action.ClickType == ClickType.Double ? 2 : 1;

        // The same key listed twice (say E and Ctrl+E) is still one physical key.
        var inputs = action.Inputs?.Where(i => i is not null).ToArray() ?? Array.Empty<InputChord>();
        var keys = inputs.Select(i => i.Key).Where(k => k != VirtualKey.None).Distinct().ToArray();
        if (keys.Length == 0)
        {
            yield break;
        }

        var modifierFlags = inputs.Aggregate(KeyModifiers.None, (all, i) => all | i.Modifiers);
        var modifiers = EnumerateModifiers(modifierFlags).ToArray();

        foreach (var modifier in modifiers)
        {
            yield return EngineStep.KeyDown(modifier, isModifier: true);
        }

        for (int i = 0; i < repeats; i++)
        {
            if (i > 0 && action.DoubleClickGapMs > 0)
            {
                yield return EngineStep.Wait(action.DoubleClickGapMs);
            }

            foreach (var key in keys)
            {
                yield return key.TryGetMouseButton(out var button)
                    ? EngineStep.MouseDown(button)
                    : EngineStep.KeyDown(key);
            }

            var hold = NextHoldDuration();
            if (hold > 0)
            {
                yield return EngineStep.Wait(hold);
            }

            for (int k = keys.Length - 1; k >= 0; k--)
            {
                // Only the very last release completes the action, so a chord of three
                // inputs is still one action and a double click is still one action.
                bool completes = i == repeats - 1 && k == 0;
                yield return keys[k].TryGetMouseButton(out var button)
                    ? EngineStep.MouseUp(button, completesAction: completes)
                    : EngineStep.KeyUp(keys[k], completesAction: completes);
            }
        }

        // Release modifiers in reverse order, the way a human would.
        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            yield return EngineStep.KeyUp(modifiers[i], isModifier: true);
        }
    }

    /// <summary>How long the next press is held: fixed, or drawn from the configured range.</summary>
    private int NextHoldDuration()
    {
        var action = _profile.Action;
        if (!action.RandomizeHoldDuration)
        {
            return Math.Max(0, action.HoldDurationMs);
        }

        int min = Math.Max(0, Math.Min(action.MinHoldDurationMs, action.MaxHoldDurationMs));
        int max = Math.Max(0, Math.Max(action.MinHoldDurationMs, action.MaxHoldDurationMs));
        return _random.NextInt(min, max);
    }

    /// <summary>Interpolated cursor travel from wherever the cursor is now to a target.</summary>
    private IEnumerable<EngineStep> MoveSteps(ScreenPoint target)
    {
        var from = _cursor.GetCursorPosition();

        foreach (var sample in MotionPlanner.Plan(from, target, _profile.Movement, _profile.Jitter, _random))
        {
            if (sample.Delay > TimeSpan.Zero)
            {
                yield return EngineStep.Wait(sample.Delay);
            }

            yield return EngineStep.MoveTo(sample.Position);
        }
    }

    /// <summary>The delay before the next action, with range randomization and timing jitter.</summary>
    private TimeSpan NextInterval()
    {
        var interval = _profile.Interval;

        double milliseconds;
        if (interval.RandomizeEnabled)
        {
            int min = Math.Max(0, Math.Min(interval.MinDelayMs, interval.MaxDelayMs));
            int max = Math.Max(0, Math.Max(interval.MinDelayMs, interval.MaxDelayMs));
            milliseconds = _random.NextInt(min, max);
        }
        else
        {
            milliseconds = Math.Max(0, interval.DelayMs);
        }

        return Jitter.ApplyTiming(TimeSpan.FromMilliseconds(milliseconds), _profile.Jitter, _random);
    }

    private ScreenPoint Clamp(ScreenPoint point) => _screen.VirtualBounds.Clamp(point);

    /// <summary>Either a bounded or an unbounded sequence, per the repeat settings.</summary>
    private IEnumerable<long> Iterations()
    {
        if (_profile.Repeat.Mode == RepeatMode.FixedCount)
        {
            long count = Math.Max(1, _profile.Repeat.Count);
            for (long i = 0; i < count; i++)
            {
                yield return i;
            }

            yield break;
        }

        for (long i = 0; ; i++)
        {
            yield return i;
        }
    }

    private static IEnumerable<VirtualKey> EnumerateModifiers(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            yield return VirtualKey.LeftControl;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            yield return VirtualKey.LeftAlt;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            yield return VirtualKey.LeftShift;
        }

        if (modifiers.HasFlag(KeyModifiers.Windows))
        {
            yield return VirtualKey.LeftWindows;
        }
    }
}
