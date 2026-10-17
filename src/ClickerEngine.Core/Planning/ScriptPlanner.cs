using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;

namespace ClickerEngine.Core.Planning;

/// <summary>
/// Turns a <see cref="ProfileKind.Script"/> profile into the same lazy stream of
/// <see cref="EngineStep"/> primitives <see cref="ActionPlanner"/> produces for a clicker, so
/// the engine runs a script with its usual hotkeys, limits, window gate and dry run.
/// <para>
/// Blocks run top to bottom; switched-off blocks are skipped with everything inside them.
/// Between two steps with no «Ждать» between them goes the automatic pause
/// (<see cref="ScriptSettings.StepDelayMs"/>); a «Ждать» replaces it. Every step a block
/// produces carries its <see cref="ScriptLocation"/>, which is how the engine knows which
/// block is running.
/// </para>
/// <para>
/// Like the clicker's, the plan is produced on demand: the cursor is read and every random
/// draw is made at the moment the engine is about to need the step. That is also where future
/// conditions belong - a "wait for a pixel colour" block would look at the screen while the
/// plan is being pulled, and yield waits until the colour shows.
/// </para>
/// </summary>
public sealed class ScriptPlanner : IRunPlanner
{
    private readonly ClickerProfile _profile;
    private readonly ICursorProvider _cursor;
    private readonly IScreenGeometry _screen;
    private readonly IRandomSource _random;

    public ScriptPlanner(
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

    /// <summary>
    /// «Нажать» and «Отпустить» are actions; «Зажать» starts one that its «Отпустить»
    /// completes. Null when the script repeats forever, or a «Стоп» could end it early.
    /// </summary>
    public long? PlannedActionCount
    {
        get
        {
            var script = _profile.Script ?? new ScriptSettings();

            try
            {
                // A «Стоп» ends the run in its first pass, however many passes there were to be.
                return Count(script.Blocks) switch
                {
                    null => null,
                    { Stops: true } pass => pass.Actions,
                    { } pass => Passes(script) is { } passes ? checked(passes * pass.Actions) : null,
                };
            }
            catch (OverflowException)
            {
                return null;
            }
        }
    }

    public IEnumerable<EngineStep> CreatePlan()
    {
        // Snapshot the script so editing it mid-run cannot corrupt the walk.
        var script = (_profile.Script ?? new ScriptSettings()).Clone();
        return PlanRun(script);
    }

    private IEnumerable<EngineStep> PlanRun(ScriptSettings script)
    {
        var state = new PlanState(script);
        long? passes = Passes(script);

        for (long pass = 1; passes is null || pass <= passes; pass++)
        {
            long before = state.Steps;
            var frame = new Frame(pass, passes, Array.Empty<LoopProgress>());

            foreach (var step in PlanBlocks(script.Blocks, frame, state))
            {
                yield return step;
            }

            // A pass that produced nothing would produce nothing forever - without ever
            // handing the engine a step at which it could notice a stop.
            if (state.Stopped || state.Steps == before)
            {
                yield break;
            }
        }
    }

    private IEnumerable<EngineStep> PlanBlocks(List<ScriptBlock>? blocks, Frame frame, PlanState state)
    {
        foreach (var block in blocks ?? Enumerable.Empty<ScriptBlock>())
        {
            if (state.Stopped)
            {
                yield break;
            }

            if (block is not { Enabled: true })
            {
                continue;
            }

            if (block is StopBlock)
            {
                state.Stopped = true;
                yield break;
            }

            var steps = block is RepeatBlock repeat
                ? PlanRepeat(repeat, frame, state)
                : PlanStep(block, frame.Locate(block), state);

            foreach (var step in steps)
            {
                yield return step;
            }
        }
    }

    private IEnumerable<EngineStep> PlanRepeat(RepeatBlock repeat, Frame frame, PlanState state)
    {
        long? count = repeat.Mode == RepeatMode.FixedCount ? Math.Max(1, repeat.Count) : null;

        for (long i = 1; count is null || i <= count; i++)
        {
            long before = state.Steps;

            foreach (var step in PlanBlocks(repeat.Blocks, frame.Enter(new LoopProgress(repeat.Id, i, count)), state))
            {
                yield return step;
            }

            // Same reasoning as for a pass: an iteration of nothing must not loop forever.
            if (state.Stopped || state.Steps == before)
            {
                yield break;
            }
        }
    }

    /// <summary>One block that does something: the pause before it if one is due, then its steps.</summary>
    private IEnumerable<EngineStep> PlanStep(ScriptBlock block, ScriptLocation location, PlanState state)
    {
        if (block is WaitBlock wait)
        {
            // An explicit wait is the gap: no automatic pause before or after it.
            state.PauseDue = false;
            state.Steps++;
            yield return EngineStep.Wait(NextWait(wait)).WithLocation(location);
            yield break;
        }

        var steps = block switch
        {
            PressBlock press => Press(press),
            HoldBlock hold => Hold(hold),
            ReleaseBlock release => Release(release),
            MoveBlock move => Move(move),

            // An unknown block never gets here past the validator; skip it rather than guess.
            _ => null,
        };

        if (steps is null)
        {
            yield break;
        }

        if (state.PauseDue)
        {
            var pause = Jitter.ApplyTiming(TimeSpan.FromMilliseconds(Math.Max(0, state.Script.StepDelayMs)), _profile.Jitter, _random);
            if (pause > TimeSpan.Zero)
            {
                state.Steps++;
                yield return EngineStep.Wait(pause);
            }
        }

        foreach (var step in steps)
        {
            state.Steps++;
            yield return step.WithLocation(location);
        }

        state.PauseDue = true;
    }

    /// <summary>Down, held for the profile's hold time, up; modifiers around it all.</summary>
    private IEnumerable<EngineStep> Press(PressBlock block)
    {
        foreach (var step in TravelTo(block.At))
        {
            yield return step;
        }

        if (block.Input is not { Key: not VirtualKey.None } input)
        {
            yield break;
        }

        var device = DeviceOf(input);
        var modifiers = PlanSteps.Modifiers(input.Modifiers);
        foreach (var modifier in modifiers)
        {
            yield return EngineStep.KeyDown(modifier, isModifier: true).WithDevice(device);
        }

        yield return PlanSteps.Down(input.Key).WithDevice(device);

        int hold = PlanSteps.HoldDuration(_profile.Action, _random);
        if (hold > 0)
        {
            yield return EngineStep.Wait(hold);
        }

        yield return PlanSteps.Up(input.Key, completesAction: true);

        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            yield return EngineStep.KeyUp(modifiers[i], isModifier: true);
        }
    }

    /// <summary>Modifiers, then the key or button, all left down.</summary>
    private IEnumerable<EngineStep> Hold(HoldBlock block)
    {
        foreach (var step in TravelTo(block.At))
        {
            yield return step;
        }

        if (block.Input is not { Key: not VirtualKey.None } input)
        {
            yield break;
        }

        var device = DeviceOf(input);
        foreach (var modifier in PlanSteps.Modifiers(input.Modifiers))
        {
            yield return EngineStep.KeyDown(modifier, isModifier: true).WithDevice(device);
        }

        yield return PlanSteps.Down(input.Key).WithDevice(device);
    }

    /// <summary>The key or button, then its modifiers, released in reverse order; completes the action.</summary>
    private IEnumerable<EngineStep> Release(ReleaseBlock block)
    {
        foreach (var step in TravelTo(block.At))
        {
            yield return step;
        }

        if (block.Input is not { Key: not VirtualKey.None } input)
        {
            yield break;
        }

        yield return PlanSteps.Up(input.Key, completesAction: true);

        var modifiers = PlanSteps.Modifiers(input.Modifiers);
        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            yield return EngineStep.KeyUp(modifiers[i], isModifier: true);
        }
    }

    /// <summary>
    /// Travel to the point. Unlike a point on an input block, a «Переместить курсор» always
    /// produces a step - an explicit move to where the cursor already is still re-pins it - so
    /// the block is reported as running.
    /// </summary>
    private IEnumerable<EngineStep> Move(MoveBlock block)
    {
        var target = Aim(block.To);
        bool moved = false;

        foreach (var step in PlanSteps.Travel(_cursor, target, _profile, _random))
        {
            moved = true;
            yield return step;
        }

        if (!moved)
        {
            yield return EngineStep.MoveTo(target);
        }
    }

    /// <summary>
    /// Which window a block's press goes to: the one under the cursor for a mouse button, the
    /// foreground one for a key. Modifiers follow the button they wrap, as in a clicker's action.
    /// </summary>
    private static ActionKind DeviceOf(InputChord input) => input.IsMouse ? ActionKind.Mouse : ActionKind.Keyboard;

    private IEnumerable<EngineStep> TravelTo(ScreenPoint? point) => point is { } at
        ? PlanSteps.Travel(_cursor, Aim(at), _profile, _random)
        : Enumerable.Empty<EngineStep>();

    /// <summary>A point the user picked, with position jitter, kept on the desktop.</summary>
    private ScreenPoint Aim(ScreenPoint point) =>
        _screen.VirtualBounds.Clamp(Jitter.ApplyPosition(point, _profile.Jitter, _random));

    private TimeSpan NextWait(WaitBlock wait)
    {
        int milliseconds;
        if (wait.RandomizeEnabled)
        {
            int min = Math.Max(0, Math.Min(wait.MinDurationMs, wait.MaxDurationMs));
            int max = Math.Max(0, Math.Max(wait.MinDurationMs, wait.MaxDurationMs));
            milliseconds = _random.NextInt(min, max);
        }
        else
        {
            milliseconds = Math.Max(0, wait.DurationMs);
        }

        return Jitter.ApplyTiming(TimeSpan.FromMilliseconds(milliseconds), _profile.Jitter, _random);
    }

    private static long? Passes(ScriptSettings script) => script.Runs switch
    {
        ScriptRunMode.Once => 1,
        ScriptRunMode.FixedCount => Math.Max(1, script.RunCount),
        _ => null,
    };

    /// <summary>
    /// Actions in one pass through <paramref name="blocks"/>, and whether a «Стоп» ends the run
    /// in it; null when the blocks repeat forever.
    /// </summary>
    private static (long Actions, bool Stops)? Count(List<ScriptBlock>? blocks)
    {
        long actions = 0;

        foreach (var block in blocks ?? Enumerable.Empty<ScriptBlock>())
        {
            switch (block)
            {
                case not { Enabled: true }:
                    break;

                case StopBlock:
                    return (actions, true);

                case PressBlock or ReleaseBlock:
                    actions++;
                    break;

                case RepeatBlock repeat:
                    if (Count(repeat.Blocks) is not { } inner)
                    {
                        return null;
                    }

                    if (inner.Stops)
                    {
                        return (actions + inner.Actions, true);
                    }

                    if (repeat.Mode == RepeatMode.Infinite)
                    {
                        return null;
                    }

                    actions = checked(actions + (Math.Max(1, repeat.Count) * inner.Actions));
                    break;
            }
        }

        return (actions, false);
    }

    /// <summary>Where the plan is: pass and enclosing repeats; immutable, so each block gets its own.</summary>
    private sealed record Frame(long Pass, long? Passes, LoopProgress[] Loops)
    {
        public ScriptLocation Locate(ScriptBlock block) => new(block.Id, Pass, Passes, Loops);

        public Frame Enter(LoopProgress loop) => this with { Loops = [.. Loops, loop] };
    }

    /// <summary>Mutable state of one run through the plan.</summary>
    private sealed class PlanState
    {
        public PlanState(ScriptSettings script) => Script = script;

        public ScriptSettings Script { get; }

        /// <summary>Steps produced so far, pauses included.</summary>
        public long Steps { get; set; }

        /// <summary>True after a step, until a «Ждать» or the automatic pause stands in for the gap.</summary>
        public bool PauseDue { get; set; }

        /// <summary>A «Стоп» was reached; nothing else runs.</summary>
        public bool Stopped { get; set; }
    }
}
