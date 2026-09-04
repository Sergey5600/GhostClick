using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Profiles;

/// <summary>
/// Stores profiles as one JSON file each under <c>&lt;root&gt;/profiles</c>, with the
/// last-used name kept in <c>&lt;root&gt;/settings.json</c>.
/// <para>
/// The settings file lives outside the profiles folder on purpose, so it can never collide
/// with a profile whose name happens to sanitize to the same file name.
/// </para>
/// </summary>
public sealed class JsonProfileStore : IProfileStore
{
    private const string ProfileExtension = ".json";
    private const string ProfilesFolderName = "profiles";
    private const string SettingsFileName = "settings.json";

    private readonly object _sync = new();

    public JsonProfileStore()
        : this(GetDefaultRootDirectory())
    {
    }

    public JsonProfileStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        ProfilesDirectory = Path.Combine(RootDirectory, ProfilesFolderName);
        SettingsPath = Path.Combine(RootDirectory, SettingsFileName);
    }

    /// <summary>Where profiles and settings live.</summary>
    public string RootDirectory { get; }

    /// <summary>The folder holding one file per profile.</summary>
    public string ProfilesDirectory { get; }

    /// <summary>The file holding the last-used profile name.</summary>
    public string SettingsPath { get; }

    /// <summary><c>%APPDATA%\GhostClick</c> on Windows, the XDG equivalent elsewhere.</summary>
    public static string GetDefaultRootDirectory()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrEmpty(appData))
        {
            appData = AppContext.BaseDirectory;
        }

        return Path.Combine(appData, "GhostClick");
    }

    public string? LastUsedProfileName
    {
        get
        {
            lock (_sync)
            {
                return ReadSettings().LastUsedProfile;
            }
        }

        set
        {
            lock (_sync)
            {
                var settings = ReadSettings();
                settings.LastUsedProfile = string.IsNullOrWhiteSpace(value) ? null : value;
                WriteSettings(settings);
            }
        }
    }

    public IReadOnlyList<string> List()
    {
        lock (_sync)
        {
            if (!Directory.Exists(ProfilesDirectory))
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            foreach (var file in Directory.EnumerateFiles(ProfilesDirectory, "*" + ProfileExtension))
            {
                // Prefer the name recorded inside the file; fall back to the file name when
                // the document is damaged, so a broken profile is still visible in the UI.
                names.Add(TryReadName(file) ?? Path.GetFileNameWithoutExtension(file));
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }

    public bool Exists(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_sync)
        {
            return File.Exists(GetProfilePath(name)) || FindByName(name) is not null;
        }
    }

    public ClickerProfile Load(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_sync)
        {
            var path = GetProfilePath(name);
            if (!File.Exists(path))
            {
                path = FindByName(name) ?? throw new ProfileNotFoundException(name);
            }

            var profile = ProfileSerializer.Deserialize(File.ReadAllText(path));

            // A file renamed on disk should not leave the profile answering to the old name.
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                profile.Name = name;
            }

            return profile;
        }
    }

    public void Save(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);

        lock (_sync)
        {
            Directory.CreateDirectory(ProfilesDirectory);
            var path = GetProfilePath(profile.Name);

            // Write to a temporary file first: a crash mid-write must not destroy the
            // previous, working profile.
            var temp = path + ".tmp";
            File.WriteAllText(temp, ProfileSerializer.Serialize(profile));
            File.Move(temp, path, overwrite: true);
        }
    }

    public bool Delete(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_sync)
        {
            var path = GetProfilePath(name);
            if (!File.Exists(path))
            {
                path = FindByName(name) ?? string.Empty;
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            File.Delete(path);

            var settings = ReadSettings();
            if (string.Equals(settings.LastUsedProfile, name, StringComparison.OrdinalIgnoreCase))
            {
                settings.LastUsedProfile = null;
                WriteSettings(settings);
            }

            return true;
        }
    }

    /// <summary>Maps a profile name onto a file name, replacing anything the OS forbids.</summary>
    internal static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[name.Length];

        for (int i = 0; i < name.Length; i++)
        {
            buffer[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
        }

        var sanitized = new string(buffer).Trim();
        return sanitized.Length == 0 ? "profile" : sanitized;
    }

    private string GetProfilePath(string name) =>
        Path.Combine(ProfilesDirectory, SanitizeFileName(name) + ProfileExtension);

    /// <summary>Finds a file whose stored profile name matches, ignoring case.</summary>
    private string? FindByName(string name)
    {
        if (!Directory.Exists(ProfilesDirectory))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(ProfilesDirectory, "*" + ProfileExtension))
        {
            if (string.Equals(TryReadName(file), name, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private static string? TryReadName(string path)
    {
        try
        {
            return ProfileSerializer.Deserialize(File.ReadAllText(path)).Name;
        }
        catch (ProfileFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private StoreSettings ReadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return new StoreSettings();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<StoreSettings>(
                File.ReadAllText(SettingsPath),
                ProfileSerializer.Options) ?? new StoreSettings();
        }
        catch (System.Text.Json.JsonException)
        {
            return new StoreSettings();
        }
        catch (IOException)
        {
            return new StoreSettings();
        }
    }

    private void WriteSettings(StoreSettings settings)
    {
        Directory.CreateDirectory(RootDirectory);
        File.WriteAllText(
            SettingsPath,
            System.Text.Json.JsonSerializer.Serialize(settings, ProfileSerializer.Options));
    }

    /// <summary>The contents of <c>settings.json</c>.</summary>
    private sealed class StoreSettings
    {
        public string? LastUsedProfile { get; set; }
    }
}

/// <summary>A store that keeps everything in memory. Used by the tests.</summary>
public sealed class InMemoryProfileStore : IProfileStore
{
    private readonly Dictionary<string, string> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public string? LastUsedProfileName { get; set; }

    public IReadOnlyList<string> List() => _profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool Exists(string name) => _profiles.ContainsKey(name);

    public ClickerProfile Load(string name) => _profiles.TryGetValue(name, out var json)
        ? ProfileSerializer.Deserialize(json)
        : throw new ProfileNotFoundException(name);

    public void Save(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles[profile.Name] = ProfileSerializer.Serialize(profile);
    }

    public bool Delete(string name)
    {
        if (!_profiles.Remove(name))
        {
            return false;
        }

        if (string.Equals(LastUsedProfileName, name, StringComparison.OrdinalIgnoreCase))
        {
            LastUsedProfileName = null;
        }

        return true;
    }
}
