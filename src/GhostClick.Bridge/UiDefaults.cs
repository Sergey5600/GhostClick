using ClickerEngine.Core.Configuration;

namespace GhostClick.Bridge;

/// <summary>What a profile created from the interface starts with.</summary>
public static class UiDefaults
{
    /// <summary>
    /// The engine defaults plus the interface's safer starting point: jitter, varied press
    /// lengths, and input that stays in the window the run started on. No run time limit
    /// either: the interface never shows one, so it must not cut a run short, «N раз» included.
    /// </summary>
    public static ClickerProfile CreateProfile(string name)
    {
        var profile = ClickerProfile.CreateDefault(name);
        profile.Jitter.PositionJitterEnabled = true;
        profile.Action.RandomizeHoldDuration = true;
        profile.Safety.ActiveWindowOnly = true;
        profile.Safety.MaxRunDurationSeconds = 0;
        return profile;
    }

    /// <summary>
    /// A new script: the same starting point, with one «Нажать» (left button, at the cursor)
    /// to build on - a script without blocks could not be saved.
    /// </summary>
    public static ClickerProfile CreateScript(string name)
    {
        var profile = CreateProfile(name);
        profile.Kind = ProfileKind.Script;
        profile.Script.Blocks.Add(new PressBlock());
        return profile;
    }
}
