using System.Text.Json.Serialization;
using ClickerEngine.Core.Configuration;

namespace GhostClick.Bridge;

/// <summary>
/// A profile's script in the page's vocabulary (see docs/scripts-ui-contract.md). Like the
/// rest of <see cref="ProfileDto"/>, the page holds it, edits it and sends it back whole.
/// </summary>
public sealed class ScriptDto
{
    /// <summary><c>once</c>, <c>fixed</c> (<see cref="Count"/> times) or <c>inf</c>.</summary>
    public string Repeat { get; set; } = "once";

    public int Count { get; set; } = 10;

    /// <summary>The automatic pause between steps, in milliseconds.</summary>
    public int StepDelay { get; set; } = 100;

    public List<BlockDto> Blocks { get; set; } = new();
}

/// <summary>
/// One block. Every block has <see cref="Type"/>, <see cref="Id"/>, <see cref="Enabled"/> and
/// <see cref="Label"/>; the other fields belong to particular types and are left out of the
/// JSON where they do not apply, or have no value.
/// </summary>
public sealed class BlockDto
{
    /// <summary><c>press</c>, <c>hold</c>, <c>release</c>, <c>move</c>, <c>wait</c>, <c>repeat</c>, <c>stop</c> - or a type from a newer version.</summary>
    public string Type { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? Label { get; set; }

    /// <summary>press, hold, release: the key or mouse button.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChordDto? Input { get; set; }

    /// <summary>press, hold, release: where to move the cursor first; none = where it is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XyDto? At { get; set; }

    /// <summary>move: where to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XyDto? To { get; set; }

    /// <summary>wait: milliseconds.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ms { get; set; }

    /// <summary>wait: draw the time from <see cref="MinMs"/>..<see cref="MaxMs"/> instead.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Rand { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxMs { get; set; }

    /// <summary>repeat: <c>fixed</c> (<see cref="Count"/> times) or <c>inf</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; set; }

    /// <summary>repeat: the blocks inside.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<BlockDto>? Blocks { get; set; }
}

/// <summary>A screen point.</summary>
public sealed class XyDto
{
    public XyDto()
    {
    }

    public XyDto(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; set; }

    public int Y { get; set; }
}

/// <summary>Converts between <see cref="ScriptSettings"/> and <see cref="ScriptDto"/>.</summary>
public static class ScriptMapper
{
    public static ScriptDto ToDto(ScriptSettings script)
    {
        ArgumentNullException.ThrowIfNull(script);

        return new ScriptDto
        {
            Repeat = script.Runs switch
            {
                ScriptRunMode.FixedCount => "fixed",
                ScriptRunMode.Infinite => "inf",
                _ => "once",
            },
            Count = script.RunCount,
            StepDelay = script.StepDelayMs,
            Blocks = ToDto(script.Blocks),
        };
    }

    /// <summary>
    /// Writes the page's script onto <paramref name="script"/>. A block of a type this build
    /// does not know keeps the data it was loaded with (matched by id); a field left out takes
    /// the value a new block of that type starts with.
    /// </summary>
    public static void Apply(ScriptDto dto, ScriptSettings script)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(script);

        script.Runs = dto.Repeat switch
        {
            "once" => ScriptRunMode.Once,
            "fixed" => ScriptRunMode.FixedCount,
            "inf" => ScriptRunMode.Infinite,
            _ => script.Runs,
        };
        script.RunCount = dto.Count;
        script.StepDelayMs = dto.StepDelay;

        if (dto.Blocks is { } blocks)
        {
            var unknown = ScriptSettings.Walk(script.Blocks, enabledOnly: false)
                .OfType<UnknownBlock>()
                .GroupBy(b => b.Id)
                .ToDictionary(g => g.Key, g => g.First());

            script.Blocks = FromDto(blocks, unknown);
        }
    }

    private static List<BlockDto> ToDto(List<ScriptBlock>? blocks) =>
        (blocks ?? new()).Where(b => b is not null).Select(ToDto).ToList();

    private static BlockDto ToDto(ScriptBlock block)
    {
        var dto = new BlockDto { Type = block.Type, Id = block.Id, Enabled = block.Enabled, Label = block.Label };

        switch (block)
        {
            case InputBlock input:
                dto.Input = input.Input is { Key: not VirtualKey.None } chord
                    ? new ChordDto(KeyCodes.ToCode(chord.Key), KeyCodes.FormatModifiers(chord.Modifiers))
                    : null;
                dto.At = input.At is { } at ? new XyDto(at.X, at.Y) : null;
                break;

            case MoveBlock move:
                dto.To = new XyDto(move.To.X, move.To.Y);
                break;

            case WaitBlock wait:
                dto.Ms = wait.DurationMs;
                dto.Rand = wait.RandomizeEnabled;
                dto.MinMs = wait.MinDurationMs;
                dto.MaxMs = wait.MaxDurationMs;
                break;

            case RepeatBlock repeat:
                dto.Mode = repeat.Mode == RepeatMode.Infinite ? "inf" : "fixed";
                dto.Count = repeat.Count;
                dto.Blocks = ToDto(repeat.Blocks);
                break;
        }

        return dto;
    }

    private static List<ScriptBlock> FromDto(List<BlockDto> blocks, Dictionary<string, UnknownBlock> unknown) =>
        blocks.Where(b => b is not null).Select(b => FromDto(b, unknown)).ToList();

    private static ScriptBlock FromDto(BlockDto dto, Dictionary<string, UnknownBlock> unknown)
    {
        ScriptBlock block = dto.Type switch
        {
            "press" => WithInput(new PressBlock(), dto),
            "hold" => WithInput(new HoldBlock(), dto),
            "release" => WithInput(new ReleaseBlock(), dto),
            "move" => new MoveBlock { To = dto.To is { } to ? new ScreenPoint(to.X, to.Y) : default },
            "wait" => FromDto(dto, new WaitBlock()),
            "repeat" => FromDto(dto, new RepeatBlock(), unknown),
            "stop" => new StopBlock(),
            _ => unknown.TryGetValue(dto.Id ?? string.Empty, out var kept) && kept.TypeName == dto.Type
                ? kept.Clone()
                : new UnknownBlock(dto.Type ?? string.Empty),
        };

        block.Id = dto.Id ?? string.Empty;
        block.Enabled = dto.Enabled;
        block.Label = dto.Label;
        return block;
    }

    private static T WithInput<T>(T block, BlockDto dto)
        where T : InputBlock
    {
        block.Input = dto.Input is { } chord && KeyCodes.TryGetVirtualKey(chord.Key, out var key)
            ? new InputChord(key, KeyCodes.ParseModifiers(chord.Mods))
            : null;
        block.At = dto.At is { } at ? new ScreenPoint(at.X, at.Y) : null;
        return block;
    }

    private static WaitBlock FromDto(BlockDto dto, WaitBlock wait)
    {
        wait.DurationMs = dto.Ms ?? wait.DurationMs;
        wait.RandomizeEnabled = dto.Rand ?? wait.RandomizeEnabled;
        wait.MinDurationMs = dto.MinMs ?? wait.MinDurationMs;
        wait.MaxDurationMs = dto.MaxMs ?? wait.MaxDurationMs;
        return wait;
    }

    private static RepeatBlock FromDto(BlockDto dto, RepeatBlock repeat, Dictionary<string, UnknownBlock> unknown)
    {
        repeat.Mode = dto.Mode switch
        {
            "fixed" => RepeatMode.FixedCount,
            "inf" => RepeatMode.Infinite,
            _ => repeat.Mode,
        };
        repeat.Count = dto.Count ?? repeat.Count;
        repeat.Blocks = dto.Blocks is { } blocks ? FromDto(blocks, unknown) : new();
        return repeat;
    }
}
