using ClickerEngine.Core.Configuration;

namespace GhostClick.Bridge;

/// <summary>What a profile created from the interface starts with.</summary>
public static class UiDefaults
{
    /// <summary>
    /// The engine defaults plus the interface's safer starting point: jitter, varied press
    /// lengths, and input that stays in the window the run started on.
    /// </summary>
    public static ClickerProfile CreateProfile(string name)
    {
        var profile = ClickerProfile.CreateDefault(name);
        profile.Jitter.PositionJitterEnabled = true;
        profile.Action.RandomizeHoldDuration = true;
        profile.Safety.ActiveWindowOnly = true;
        return profile;
    }
}
