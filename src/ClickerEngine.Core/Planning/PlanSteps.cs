using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;

namespace ClickerEngine.Core.Planning;

/// <summary>Step-building pieces <see cref="ActionPlanner"/> and <see cref="ScriptPlanner"/> share.</summary>
internal static class PlanSteps
{
    /// <summary>The keys that hold down a set of modifiers, in the order they are pressed.</summary>
    public static VirtualKey[] Modifiers(KeyModifiers modifiers)
    {
        var keys = new List<VirtualKey>(4);

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            keys.Add(VirtualKey.LeftControl);
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            keys.Add(VirtualKey.LeftAlt);
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            keys.Add(VirtualKey.LeftShift);
        }

        if (modifiers.HasFlag(KeyModifiers.Windows))
        {
            keys.Add(VirtualKey.LeftWindows);
        }

        return keys.ToArray();
    }

    /// <summary>The press of a key or mouse button.</summary>
    public static EngineStep Down(VirtualKey key) => key.TryGetMouseButton(out var button)
        ? EngineStep.MouseDown(button)
        : EngineStep.KeyDown(key);

    /// <summary>The release of a key or mouse button.</summary>
    public static EngineStep Up(VirtualKey key, bool completesAction) => key.TryGetMouseButton(out var button)
        ? EngineStep.MouseUp(button, completesAction)
        : EngineStep.KeyUp(key, completesAction: completesAction);

    /// <summary>
    /// How long the next press of <paramref name="input"/> is held: its own press time, or -
    /// with <see cref="ActionSettings.RandomizeHoldDuration"/> - a random time up to
    /// <see cref="ActionSettings.PressSpreadPercent"/> shorter or longer.
    /// </summary>
    public static int PressDuration(InputChord input, ActionSettings action, IRandomSource random)
    {
        int press = Math.Max(0, input.PressMs);
        if (!action.RandomizeHoldDuration || press == 0)
        {
            return press;
        }

        int spread = (int)Math.Round(press * Math.Clamp(action.PressSpreadPercent, 0, 100) / 100.0);
        return random.NextInt(press - spread, press + spread);
    }

    /// <summary>
    /// Interpolated cursor travel from wherever the cursor is when this is enumerated to
    /// <paramref name="target"/>. Empty when the cursor is already there.
    /// </summary>
    public static IEnumerable<EngineStep> Travel(
        ICursorProvider cursor,
        ScreenPoint target,
        ClickerProfile profile,
        IRandomSource random)
    {
        var from = cursor.GetCursorPosition();

        foreach (var sample in MotionPlanner.Plan(from, target, profile.Movement, profile.Jitter, random))
        {
            if (sample.Delay > TimeSpan.Zero)
            {
                yield return EngineStep.Wait(sample.Delay);
            }

            yield return EngineStep.MoveTo(sample.Position);
        }
    }
}
