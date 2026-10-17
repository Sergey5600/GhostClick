using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ClickerEngine.Core.Configuration;

/// <summary>
/// One step of a script, in the spirit of an iOS Shortcuts action: a small card the user
/// stacks, reorders, switches off and labels.
/// <para>
/// Blocks are plain configuration. <see cref="Planning.ScriptPlanner"/> turns them into the
/// engine's usual primitives, so a new kind of block - a condition such as "wait for a pixel
/// colour", or typing text - is a new subclass, a JSON type name in
/// <see cref="ScriptBlockJsonConverter"/>, a case in the planner and the validator; the
/// engine does not change. A file holding a block this build does not know still loads: the
/// block becomes an <see cref="UnknownBlock"/> that keeps its data and fails validation.
/// </para>
/// </summary>
[JsonConverter(typeof(ScriptBlockJsonConverter))]
public abstract class ScriptBlock
{
    /// <summary>
    /// Identifies the block for as long as it exists, wherever it is moved: the engine reports
    /// the running block and the validator reports problems by this id. Unique in a script.
    /// </summary>
    [JsonPropertyOrder(-3)]
    public string Id { get; set; } = NewId();

    /// <summary>A switched-off block is skipped, together with everything inside it, and not validated.</summary>
    [JsonPropertyOrder(-2)]
    public bool Enabled { get; set; } = true;

    /// <summary>Optional caption the user gives the block; purely for the UI.</summary>
    [JsonPropertyOrder(-1)]
    public string? Label { get; set; }

    /// <summary>The block's type name in JSON, e.g. <c>press</c>.</summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>A deep copy, children included.</summary>
    public abstract ScriptBlock Clone();

    /// <summary>A fresh id: eight hex digits, plenty for the blocks of one script.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    public override string ToString() => Label is { Length: > 0 } ? $"{Type} '{Label}'" : $"{Type} {Id}";
}

/// <summary>A block that holds other blocks: today <see cref="RepeatBlock"/>, later conditions.</summary>
public interface IBlockContainer
{
    List<ScriptBlock> Blocks { get; set; }
}

/// <summary>
/// The shared shape of «Нажать», «Зажать» and «Отпустить»: a key or mouse button with its
/// modifiers, optionally at a point the cursor is moved to first.
/// </summary>
public abstract class InputBlock : ScriptBlock
{
    /// <summary>What to press. Null until the user picks something, which fails validation.</summary>
    public InputChord? Input { get; set; } = InputChord.Mouse(MouseButton.Left);

    /// <summary>When set, the cursor travels here (by the profile's movement settings) first.</summary>
    public ScreenPoint? At { get; set; }

    /// <summary>Copies the base and input fields onto <paramref name="clone"/>.</summary>
    protected T CopyTo<T>(T clone)
        where T : InputBlock
    {
        clone.Id = Id;
        clone.Enabled = Enabled;
        clone.Label = Label;
        clone.Input = Input?.Clone();
        clone.At = At;
        return clone;
    }
}

/// <summary>«Нажать»: press and release, held for the input's own press time.</summary>
public sealed class PressBlock : InputBlock
{
    public override string Type => "press";

    public override ScriptBlock Clone() => CopyTo(new PressBlock());
}

/// <summary>«Зажать»: press and leave it down, for drags and held keys. The run releases it at the latest when it ends.</summary>
public sealed class HoldBlock : InputBlock
{
    public override string Type => "hold";

    public override ScriptBlock Clone() => CopyTo(new HoldBlock());
}

/// <summary>«Отпустить»: release what a «Зажать» left down.</summary>
public sealed class ReleaseBlock : InputBlock
{
    public override string Type => "release";

    public override ScriptBlock Clone() => CopyTo(new ReleaseBlock());
}

/// <summary>«Переместить курсор»: to an absolute point, by the profile's movement settings.</summary>
public sealed class MoveBlock : ScriptBlock
{
    public override string Type => "move";

    public ScreenPoint To { get; set; }

    public override ScriptBlock Clone() => (MoveBlock)MemberwiseClone();
}

/// <summary>«Ждать»: a fixed time, or a random one from a range. Replaces the automatic pause.</summary>
public sealed class WaitBlock : ScriptBlock
{
    public override string Type => "wait";

    /// <summary>How long to wait, in milliseconds.</summary>
    public int DurationMs { get; set; } = 1000;

    /// <summary>When true the wait is drawn from [<see cref="MinDurationMs"/>, <see cref="MaxDurationMs"/>] instead.</summary>
    public bool RandomizeEnabled { get; set; }

    public int MinDurationMs { get; set; } = 500;

    public int MaxDurationMs { get; set; } = 1500;

    public override ScriptBlock Clone() => (WaitBlock)MemberwiseClone();
}

/// <summary>«Повторить»: the blocks inside, a number of times or until the run is stopped.</summary>
public sealed class RepeatBlock : ScriptBlock, IBlockContainer
{
    public override string Type => "repeat";

    public RepeatMode Mode { get; set; } = RepeatMode.FixedCount;

    /// <summary>Iterations when <see cref="Mode"/> is <see cref="RepeatMode.FixedCount"/>.</summary>
    public int Count { get; set; } = 2;

    public List<ScriptBlock> Blocks { get; set; } = new();

    public override ScriptBlock Clone()
    {
        var clone = (RepeatBlock)MemberwiseClone();
        clone.Blocks = ScriptSettings.CloneBlocks(Blocks);
        return clone;
    }
}

/// <summary>«Стоп»: ends the whole run here, every remaining pass included.</summary>
public sealed class StopBlock : ScriptBlock
{
    public override string Type => "stop";

    public override ScriptBlock Clone() => (StopBlock)MemberwiseClone();
}

/// <summary>
/// A block whose type this build does not know - written by a newer GhostClick, or a typo in a
/// hand-edited file. It keeps everything it was read with and writes it back unchanged, so
/// opening and saving such a profile loses nothing; the validator refuses to run it.
/// </summary>
public sealed class UnknownBlock : ScriptBlock
{
    public UnknownBlock(string typeName, JsonObject? data = null)
    {
        TypeName = typeName ?? string.Empty;
        Data = data ?? new JsonObject();
    }

    /// <summary>The type name as read; empty when the block had none.</summary>
    [JsonIgnore]
    public string TypeName { get; }

    /// <summary>The block's own fields, everything but type, id, enabled and label.</summary>
    [JsonIgnore]
    public JsonObject Data { get; }

    public override string Type => TypeName;

    public override ScriptBlock Clone() => new UnknownBlock(TypeName, (JsonObject)Data.DeepClone())
    {
        Id = Id,
        Enabled = Enabled,
        Label = Label,
    };
}

/// <summary>
/// Reads and writes <see cref="ScriptBlock"/> as a JSON object tagged by <c>type</c>.
/// <para>
/// Hand-written rather than <c>[JsonPolymorphic]</c> for two reasons: the tag may sit anywhere
/// in the object (a hand-edited file need not keep it first), and an unknown tag becomes an
/// <see cref="UnknownBlock"/> instead of failing the whole profile.
/// </para>
/// </summary>
public sealed class ScriptBlockJsonConverter : JsonConverter<ScriptBlock>
{
    private const string TypeProperty = "type";

    private static readonly Dictionary<string, Type> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["press"] = typeof(PressBlock),
        ["hold"] = typeof(HoldBlock),
        ["release"] = typeof(ReleaseBlock),
        ["move"] = typeof(MoveBlock),
        ["wait"] = typeof(WaitBlock),
        ["repeat"] = typeof(RepeatBlock),
        ["stop"] = typeof(StopBlock),
    };

    /// <summary>The type names this build understands.</summary>
    public static IReadOnlyCollection<string> KnownTypes => Types.Keys;

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(ScriptBlock);

    public override ScriptBlock? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (JsonNode.Parse(ref reader) is not JsonObject block)
        {
            throw new JsonException("A script block must be a JSON object.");
        }

        var typeName = block[TypeProperty] is JsonValue tag && tag.TryGetValue<string>(out var name) ? name : string.Empty;
        block.Remove(TypeProperty);

        if (Types.TryGetValue(typeName, out var type))
        {
            return (ScriptBlock?)block.Deserialize(type, options);
        }

        // The base fields go where every block keeps them, so the interface can show, switch
        // off and caption the block, and errors can name it; the rest is kept as it came.
        var unknown = new UnknownBlock(typeName);
        foreach (var (property, value) in block.ToArray())
        {
            block.Remove(property);
            switch (property.ToLowerInvariant())
            {
                case "id":
                    unknown.Id = value is JsonValue id && id.TryGetValue<string>(out var text) ? text : string.Empty;
                    break;
                case "enabled":
                    unknown.Enabled = value is not JsonValue enabled || !enabled.TryGetValue<bool>(out var on) || on;
                    break;
                case "label":
                    unknown.Label = value is JsonValue label && label.TryGetValue<string>(out var caption) ? caption : null;
                    break;
                default:
                    unknown.Data[property] = value;
                    break;
            }
        }

        return unknown;
    }

    public override void Write(Utf8JsonWriter writer, ScriptBlock value, JsonSerializerOptions options)
    {
        IEnumerable<KeyValuePair<string, JsonNode?>> fields;
        if (value is UnknownBlock unknown)
        {
            fields = Base(unknown, options).Concat(unknown.Data);
        }
        else
        {
            var known = JsonSerializer.SerializeToNode(value, value.GetType(), options)!.AsObject();

            // [JsonIgnore] on the abstract Type does not reach the overrides; the tag goes first instead.
            known.Remove(Name(nameof(ScriptBlock.Type), options));
            fields = known;
        }

        writer.WriteStartObject();
        writer.WriteString(TypeProperty, value.Type);
        foreach (var (name, field) in fields)
        {
            writer.WritePropertyName(name);
            if (field is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                field.WriteTo(writer, options);
            }
        }

        writer.WriteEndObject();
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Base(ScriptBlock block, JsonSerializerOptions options)
    {
        yield return new(Name("Id", options), JsonValue.Create(block.Id));
        yield return new(Name("Enabled", options), JsonValue.Create(block.Enabled));
        yield return new(Name("Label", options), JsonValue.Create(block.Label));
    }

    private static string Name(string property, JsonSerializerOptions options) =>
        options.PropertyNamingPolicy?.ConvertName(property) ?? property;
}
