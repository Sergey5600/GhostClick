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
            // is on, in which case we nudge around the current spot on purpose. A tick that
            // presses nothing has no spot to aim at, and nudging would drag a held button.
            if (_profile.Jitter.PositionJitterEnabled && TickEntries().Length > 0)
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
    /// The press/release steps for one action tick. Every input the tick presses goes down at
    /// its start, in list order. A click comes up after its press time - and with a double
    /// click goes down again once every first click is up and the gap has passed - and a timed
    /// hold comes up after its hold time. Releases due at the same moment go in reverse list
    /// order. The modifiers of all of them wrap the whole tick, except the ones an input held
    /// for the run already keeps down. The last release completes the action, so a chord, a
    /// double click and a tick that mixes clicks and holds are each still one action; a tick
    /// with nothing to press is a single <see cref="EngineStep.Tick"/>.
    /// </summary>
    private IEnumerable<EngineStep> EmitAction()
    {
        var entries = TickEntries();
        var pressed = entries.DistinctBy(i => i.Key).ToArray();
        if (pressed.Length == 0)
        {
            yield return EngineStep.Tick();
            yield break;
        }

        var heldModifiers = _profile.Action.HeldWhileRunning.Aggregate(KeyModifiers.None, (all, i) => all | i.Modifiers);
        var modifierFlags = entries.Aggregate(KeyModifiers.None, (all, i) => all | i.Modifiers) & ~heldModifiers;
        var modifiers = EnumerateModifiers(modifierFlags).ToArray();

        foreach (var modifier in modifiers)
        {
            yield return EngineStep.KeyDown(modifier, isModifier: true);
        }

        var events = Timeline(pressed);
        long now = 0;

        for (int e = 0; e < events.Count; e++)
        {
            var press = events[e];
            if (press.Time > now)
            {
                yield return EngineStep.Wait(TimeSpan.FromMilliseconds(press.Time - now));
                now = press.Time;
            }

            var key = pressed[press.Input].Key;
            if (press.Down)
            {
                yield return key.TryGetMouseButton(out var button)
                    ? EngineStep.MouseDown(button)
                    : EngineStep.KeyDown(key);
            }
            else
            {
                bool completes = e == events.Count - 1;
                yield return key.TryGetMouseButton(out var button)
                    ? EngineStep.MouseUp(button, completesAction: completes)
                    : EngineStep.KeyUp(key, completesAction: completes);
            }
        }

        // Release modifiers in reverse order, the way a human would.
        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            yield return EngineStep.KeyUp(modifiers[i], isModifier: true);
        }
    }

    /// <summary>
    /// The entries an action tick presses: everything but the inputs held for the whole run,
    /// and any other entry for a key one of those already holds down. The same key listed
    /// twice (say E and Ctrl+E) is still one physical key: it is pressed the way its first
    /// entry says, inside the modifiers of both.
    /// </summary>
    private InputChord[] TickEntries()
    {
        var inputs = _profile.Action.Inputs?.Where(i => i is not null && i.Key != VirtualKey.None).ToArray()
            ?? Array.Empty<InputChord>();
        var held = inputs.Where(i => i.IsHeldWhileRunning).Select(i => i.Key).ToHashSet();

        return inputs.Where(i => !i.IsHeldWhileRunning && !held.Contains(i.Key)).ToArray();
    }

    /// <summary>
    /// When each input of a tick goes down and comes up, in milliseconds from the start of the
    /// tick, in the order they happen.
    /// </summary>
    private List<PressEvent> Timeline(InputChord[] inputs)
    {
        var events = new List<PressEvent>(inputs.Length * 4);
        var clicks = new List<int>(inputs.Length);
        long firstClicksUp = 0;

        for (int i = 0; i < inputs.Length; i++)
        {
            events.Add(new PressEvent(0, 0, true, i));

            if (inputs[i].Mode == PressMode.Hold)
            {
                events.Add(new PressEvent(Math.Max(0, inputs[i].HoldMs), 0, false, i));
                continue;
            }

            long up = NextPressDuration(inputs[i]);
            events.Add(new PressEvent(up, 0, false, i));
            firstClicksUp = Math.Max(firstClicksUp, up);
            clicks.Add(i);
        }

        // A double click clicks twice; a hold is held once.
        if (_profile.Action.ClickType == ClickType.Double && clicks.Count > 0)
        {
            long second = firstClicksUp + Math.Max(0, _profile.Action.DoubleClickGapMs);
            foreach (int i in clicks)
            {
                events.Add(new PressEvent(second, 1, true, i));
                events.Add(new PressEvent(second + NextPressDuration(inputs[i]), 1, false, i));
            }
        }

        // By time; at the same moment the first click before the second, a press before its
        // own release (a zero-length press), presses in list order and releases in reverse.
        events.Sort((a, b) =>
            a.Time != b.Time ? a.Time.CompareTo(b.Time)
            : a.Round != b.Round ? a.Round.CompareTo(b.Round)
            : a.Down != b.Down ? (a.Down ? -1 : 1)
            : a.Down ? a.Input.CompareTo(b.Input) : b.Input.CompareTo(a.Input));

        return events;
    }

    /// <summary>How long the next click of <paramref name="input"/> is held: its press time, or a random time around it.</summary>
    private long NextPressDuration(InputChord input)
    {
        int press = Math.Max(0, input.PressMs);
        var action = _profile.Action;
        if (!action.RandomizeHoldDuration || press == 0)
        {
            return press;
        }

        int spread = (int)Math.Round(press * Math.Clamp(action.PressSpreadPercent, 0, 100) / 100.0);
        return _random.NextInt(press - spread, press + spread);
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

    /// <summary>One press or release within an action tick.</summary>
    /// <param name="Time">Milliseconds from the start of the tick.</param>
    /// <param name="Round">0 for the first click, 1 for the second click of a double click.</param>
    /// <param name="Down">Press or release.</param>
    /// <param name="Input">Index into the tick's inputs.</param>
    private readonly record struct PressEvent(long Time, int Round, bool Down, int Input);

    /// <summary>The virtual keys for a set of modifier flags, in the order they are pressed.</summary>
    internal static IEnumerable<VirtualKey> EnumerateModifiers(KeyModifiers modifiers)
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
