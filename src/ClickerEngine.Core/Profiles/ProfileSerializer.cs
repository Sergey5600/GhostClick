using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

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
            var root = JsonNode.Parse(json, documentOptions: DocumentOptions);
            if (root is null)
            {
                throw new ProfileFormatException("The profile document was empty.");
            }

            if (root is not JsonObject document)
            {
                throw new ProfileFormatException("The profile document is not a JSON object.");
            }

            MigrateSingleAction(document);
            StampCurrentVersion(document);

            return document.Deserialize<ClickerProfile>(Options)
                ?? throw new ProfileFormatException("The profile document was empty.");
        }
        catch (JsonException ex)
        {
            throw new ProfileFormatException("The profile document is not valid JSON.", ex);
        }
    }

    /// <summary>A deep copy via a JSON round trip; used where reference sharing would bite.</summary>
    public static ClickerProfile DeepCopy(ClickerProfile profile) => Deserialize(Serialize(profile));

    /// <summary>
    /// Schema 1 described the action as one button (<c>kind: mouse</c>) or one key with
    /// modifiers (<c>kind: keyboard</c>). Schema 2 has a list of inputs instead; an old file
    /// becomes a list holding that single input, so it keeps doing exactly what it did.
    /// </summary>
    private static void MigrateSingleAction(JsonObject document)
    {
        if (document["action"] is not JsonObject action || action.ContainsKey("inputs"))
        {
            return;
        }

        var chord = Read(action["kind"], ActionKind.Mouse) == ActionKind.Keyboard
            ? new InputChord(Read(action["key"], VirtualKey.Space), Read(action["keyModifiers"], KeyModifiers.None))
            : InputChord.Mouse(Read(action["button"], MouseButton.Left));

        action["inputs"] = JsonSerializer.SerializeToNode(new List<InputChord> { chord }, Options);
        action.Remove("kind");
        action.Remove("button");
        action.Remove("key");
        action.Remove("keyModifiers");
    }

    /// <summary>
    /// Version 3 only added fields (<c>kind</c>, <c>script</c>) whose defaults are what an older
    /// file meant: a clicker. Such a file needs no migration, but once read it is a version 3
    /// profile, and is written back as one.
    /// </summary>
    private static void StampCurrentVersion(JsonObject document)
    {
        if (Read(document["version"], 1) < ClickerProfile.CurrentVersion)
        {
            document["version"] = ClickerProfile.CurrentVersion;
        }
    }

    private static T Read<T>(JsonNode? node, T fallback) =>
        node is null ? fallback : node.Deserialize<T>(Options) ?? fallback;

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
