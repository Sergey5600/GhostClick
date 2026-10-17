using System.Text.Json;
using ClickerEngine.Core.Profiles;

namespace GhostClick.Bridge;

/// <summary>
/// The user's interface choices that belong to no profile: switching profiles does not
/// change them.
/// </summary>
public sealed class UiSettings
{
    /// <summary>The interface language, <c>ru</c> or <c>en</c>.</summary>
    public string Language { get; set; } = UiText.Russian.Language;

    public UiSettings Clone() => (UiSettings)MemberwiseClone();
}

/// <summary>Where <see cref="UiSettings"/> are kept between runs.</summary>
public interface IUiSettingsStore
{
    /// <summary>The saved settings, or the defaults when there are none or they cannot be read.</summary>
    UiSettings Load();

    /// <summary>Saves the settings. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> on failure.</summary>
    void Save(UiSettings settings);
}

/// <summary>
/// Keeps <see cref="UiSettings"/> in <c>interface.json</c> next to the profiles
/// (<c>%APPDATA%\GhostClick</c>), so uninstalling asks about it together with them.
/// </summary>
public sealed class JsonUiSettingsStore : IUiSettingsStore
{
    private const string FileName = "interface.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public JsonUiSettingsStore()
        : this(JsonProfileStore.GetDefaultRootDirectory())
    {
    }

    public JsonUiSettingsStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        FilePath = Path.Combine(Path.GetFullPath(rootDirectory), FileName);
    }

    /// <summary>The settings file.</summary>
    public string FilePath { get; }

    public UiSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new UiSettings();
            }

            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath), Options) ?? new UiSettings();
            if (!UiText.IsLanguage(settings.Language))
            {
                settings.Language = UiText.Russian.Language;
            }

            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or unreadable file costs the user their language choice, not the app.
            return new UiSettings();
        }
    }

    public void Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}

/// <summary>Keeps <see cref="UiSettings"/> in memory. Used by the tests.</summary>
public sealed class InMemoryUiSettingsStore : IUiSettingsStore
{
    private UiSettings _settings = new();

    public UiSettings Load() => _settings.Clone();

    public void Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.Clone();
    }
}
