using ClickerEngine.Core.Configuration;
using ClickerEngine.Core.Input;
using ClickerEngine.Core.Motion;

namespace ClickerEngine.Core.Planning;

/// <summary>Turns a profile into the stream of primitives the engine executes.</summary>
public interface IRunPlanner
{
    /// <summary>The full plan for one run, produced lazily; endless for an endless run.</summary>
    IEnumerable<EngineStep> CreatePlan();

    /// <summary>
    /// The number of actions a run will perform, or <see langword="null"/> when that is
    /// unbounded or cannot be known in advance. Used by the UI for a progress readout.
    /// </summary>
    long? PlannedActionCount { get; }
}

/// <summary>Picks the planner for a profile's kind.</summary>
public static class RunPlanner
{
    public static IRunPlanner For(
        ClickerProfile profile,
        ICursorProvider cursor,
        IScreenGeometry screen,
        IRandomSource? random = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Kind == ProfileKind.Script
            ? new ScriptPlanner(profile, cursor, screen, random)
            : new ActionPlanner(profile, cursor, screen, random);
    }
}
