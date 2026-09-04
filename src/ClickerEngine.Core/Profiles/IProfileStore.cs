using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Profiles;

/// <summary>
/// Persistence for named profiles. Abstracted so the dev UI can use files while a test (or a
/// future hosted front end) uses memory.
/// </summary>
public interface IProfileStore
{
    /// <summary>Names of every stored profile, sorted.</summary>
    IReadOnlyList<string> List();

    /// <summary>True when a profile with that name exists.</summary>
    bool Exists(string name);

    /// <summary>Loads a profile. Throws <see cref="ProfileNotFoundException"/> when missing.</summary>
    ClickerProfile Load(string name);

    /// <summary>Creates or overwrites a profile, using <see cref="ClickerProfile.Name"/> as the key.</summary>
    void Save(ClickerProfile profile);

    /// <summary>Deletes a profile. Returns false when it did not exist.</summary>
    bool Delete(string name);

    /// <summary>The profile to restore on the next start, or null if none was recorded.</summary>
    string? LastUsedProfileName { get; set; }
}

/// <summary>Thrown when a named profile is not in the store.</summary>
public sealed class ProfileNotFoundException : Exception
{
    public ProfileNotFoundException(string name)
        : base($"Profile '{name}' was not found.")
    {
        ProfileName = name;
    }

    public string ProfileName { get; }
}
