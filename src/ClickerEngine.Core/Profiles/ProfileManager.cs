using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Profiles;

/// <summary>Reports that the current profile was swapped out.</summary>
public sealed class CurrentProfileChangedEventArgs : EventArgs
{
    public CurrentProfileChangedEventArgs(ClickerProfile profile) => Profile = profile;

    public ClickerProfile Profile { get; }
}

/// <summary>
/// The "which profile am I editing" layer over an <see cref="IProfileStore"/>: keeps one
/// profile current, remembers it as the last used, and restores it on the next start.
/// </summary>
public sealed class ProfileManager
{
    private readonly IProfileStore _store;
    private ClickerProfile _current;

    public ProfileManager(IProfileStore store, ClickerProfile? initial = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = initial ?? ClickerProfile.CreateDefault();
    }

    /// <summary>Raised when <see cref="Current"/> is replaced, by a load or a new profile.</summary>
    public event EventHandler<CurrentProfileChangedEventArgs>? CurrentProfileChanged;

    /// <summary>The profile being edited and run.</summary>
    public ClickerProfile Current
    {
        get => _current;
        private set
        {
            _current = value;
            CurrentProfileChanged?.Invoke(this, new CurrentProfileChangedEventArgs(value));
        }
    }

    /// <summary>Every stored profile name.</summary>
    public IReadOnlyList<string> AvailableProfiles => _store.List();

    /// <summary>
    /// Restores the last used profile at start-up. Falls back to the first stored profile,
    /// then to a freshly created default (which is saved so the store is never empty).
    /// </summary>
    public ClickerProfile LoadLastUsedOrDefault()
    {
        var lastUsed = _store.LastUsedProfileName;

        if (!string.IsNullOrWhiteSpace(lastUsed) && TryLoad(lastUsed, out var profile))
        {
            return profile;
        }

        var available = _store.List();
        if (available.Count > 0 && TryLoad(available[0], out var first))
        {
            return first;
        }

        var fallback = ClickerProfile.CreateDefault();
        Current = fallback;
        Save();
        return fallback;
    }

    /// <summary>Loads a stored profile and makes it current.</summary>
    public ClickerProfile Load(string name)
    {
        var profile = _store.Load(name);
        Current = profile;
        _store.LastUsedProfileName = profile.Name;
        return profile;
    }

    /// <summary>Loads a profile, returning false instead of throwing when it is missing or broken.</summary>
    public bool TryLoad(string name, out ClickerProfile profile)
    {
        try
        {
            profile = Load(name);
            return true;
        }
        catch (ProfileNotFoundException)
        {
            profile = Current;
            return false;
        }
        catch (ProfileFormatException)
        {
            profile = Current;
            return false;
        }
    }

    /// <summary>Persists the current profile and records it as last used.</summary>
    public void Save()
    {
        ProfileValidator.EnsureValid(Current);
        _store.Save(Current);
        _store.LastUsedProfileName = Current.Name;
    }

    /// <summary>Saves the current profile under a new name, which then becomes current.</summary>
    public ClickerProfile SaveAs(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var copy = Current.Clone();
        copy.Name = name;
        Current = copy;
        Save();
        return copy;
    }

    /// <summary>Creates a fresh default profile under the given name and makes it current.</summary>
    public ClickerProfile CreateNew(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var profile = ClickerProfile.CreateDefault(name);
        Current = profile;
        Save();
        return profile;
    }

    /// <summary>
    /// Deletes a stored profile. Deleting the current one leaves it loaded in memory so the
    /// user does not lose what is on screen.
    /// </summary>
    public bool Delete(string name) => _store.Delete(name);
}
