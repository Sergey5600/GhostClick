using System.Text.Json;
using System.Text.Json.Serialization;
using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Profiles;

/// <summary>
/// JSON encoding for profiles. Enums are written as names, so a saved file stays readable
/// and hand-editable, and stays valid if the enum values are ever renumbered.
/// </summary>
public static class ProfileSerializer
{
    /// <summary>The options every read and write goes through.</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ClickerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return JsonSerializer.Serialize(profile, Options);
    }

    /// <summary>Throws <see cref="ProfileFormatException"/> when the JSON is not a profile.</summary>
    public static ClickerProfile Deserialize(string json)
    {
        // An empty file - a zero-byte leftover from a crash, say - is a format problem, not
        // a caller mistake, so it surfaces the same way as any other unreadable profile.
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ProfileFormatException("The profile document was empty.");
        }

        try
        {
            return JsonSerializer.Deserialize<ClickerProfile>(json, Options)
                ?? throw new ProfileFormatException("The profile document was empty.");
        }
        catch (JsonException ex)
        {
            throw new ProfileFormatException("The profile document is not valid JSON.", ex);
        }
    }

    /// <summary>A deep copy via a JSON round trip; used where reference sharing would bite.</summary>
    public static ClickerProfile DeepCopy(ClickerProfile profile) => Deserialize(Serialize(profile));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
        return options;
    }
}

/// <summary>Thrown when a profile file cannot be parsed.</summary>
public sealed class ProfileFormatException : Exception
{
    public ProfileFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
