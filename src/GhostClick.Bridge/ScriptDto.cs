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
    /// <summary>
    /// <c>press</c>, <c>hold</c>, <c>release</c>, <c>holdFor</c>, <c>scroll</c>, <c>drag</c>,
    /// <c>type</c>, <c>move</c>, <c>shift</c>, <c>wait</c>, <c>waitKey</c>, <c>repeat</c>,
    /// <c>random</c>, <c>stop</c>, <c>label</c> - or a type from a newer version.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string? Label { get; set; }

    /// <summary>press, hold, release, holdFor, waitKey: the key or mouse button; drag: the mouse button.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChordDto? Input { get; set; }

    /// <summary>press: how long the key is held down, in milliseconds; none = 30.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Press { get; set; }

    /// <summary>press: <c>single</c> or <c>double</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Click { get; set; }

    /// <summary>press: <c>profile</c> (as long as the profile's clicks) or <c>own</c> (<see cref="Press"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Dur { get; set; }

    /// <summary>press, hold, release, scroll: where to move the cursor first; none = where it is. drag: where it starts.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XyDto? At { get; set; }

    /// <summary>move, drag: where to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XyDto? To { get; set; }

    /// <summary>move: <c>instant</c> or <c>smooth</c> (in <see cref="Ms"/>); none = as the profile moves.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? How { get; set; }

    /// <summary>scroll: <c>up</c> or <c>down</c>, by <see cref="Count"/> notches.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Dir { get; set; }

    /// <summary>drag: <c>slow</c>, <c>mid</c> or <c>fast</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Speed { get; set; }

    /// <summary>type: what to type; label: the heading.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>shift: the offset, in pixels.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Dx { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Dy { get; set; }

    /// <summary>wait: <c>ms</c> or <c>s</c> - only how the interface shows the times, which stay milliseconds.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unit { get; set; }

    /// <summary>wait, holdFor: milliseconds; type: between characters; move: travel time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ms { get; set; }

    /// <summary>wait, holdFor: draw the time from <see cref="MinMs"/>..<see cref="MaxMs"/> instead.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Rand { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MinMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxMs { get; set; }

    /// <summary>repeat: <c>fixed</c> (<see cref="Count"/> times) or <c>inf</c>. scroll's notches are in <see cref="Count"/> too.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Count { get; set; }

    /// <summary>repeat, random: the blocks inside.</summary>
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
                dto.Input = Chord(input.Input);
                dto.Press = block is PressBlock && dto.Input is not null ? input.Input!.PressMs : null;
                dto.At = Xy(input.At);

                if (block is PressBlock press)
                {
                    dto.Click = press.Click == ClickType.Double ? "double" : "single";
                    dto.Dur = press.ProfilePress ? "profile" : "own";
                }
                else if (block is HoldForBlock holdFor)
                {
                    dto.Ms = holdFor.DurationMs;
                    dto.Rand = holdFor.RandomizeEnabled;
                    dto.MinMs = holdFor.MinDurationMs;
                    dto.MaxMs = holdFor.MaxDurationMs;
                }

                break;

            case ScrollBlock scroll:
                dto.Dir = scroll.Direction == ScrollDirection.Up ? "up" : "down";
                dto.Count = scroll.Notches;
                dto.At = Xy(scroll.At);
                break;

            case DragBlock drag:
                dto.Input = new ChordDto(KeyCodes.ToCode(drag.Button.ToVirtualKey()), Array.Empty<string>());
                dto.At = new XyDto(drag.From.X, drag.From.Y);
                dto.To = new XyDto(drag.To.X, drag.To.Y);
                dto.Speed = drag.Speed switch
                {
                    DragSpeed.Slow => "slow",
                    DragSpeed.Fast => "fast",
                    _ => "mid",
                };
                break;

            case TypeTextBlock type:
                dto.Text = type.Text;
                dto.Ms = type.CharDelayMs;
                break;

            case MoveBlock move:
                dto.To = new XyDto(move.To.X, move.To.Y);
                dto.How = move.Movement switch
                {
                    MovementMode.Instant => "instant",
                    MovementMode.Smooth => "smooth",
                    _ => null,
                };
                dto.Ms = move.DurationMs;
                break;

            case ShiftBlock shift:
                dto.Dx = shift.Dx;
                dto.Dy = shift.Dy;
                break;

            case WaitBlock wait:
                dto.Ms = wait.DurationMs;
                dto.Rand = wait.RandomizeEnabled;
                dto.MinMs = wait.MinDurationMs;
                dto.MaxMs = wait.MaxDurationMs;
                dto.Unit = wait.InSeconds ? "s" : "ms";
                break;

            case WaitKeyBlock waitKey:
                dto.Input = Chord(waitKey.Input);
                break;

            case RepeatBlock repeat:
                dto.Mode = repeat.Mode == RepeatMode.Infinite ? "inf" : "fixed";
                dto.Count = repeat.Count;
                dto.Blocks = ToDto(repeat.Blocks);
                break;

            case RandomBlock random:
                dto.Blocks = ToDto(random.Blocks);
                break;

            case LabelBlock label:
                dto.Text = label.Text;
                break;
        }

        return dto;
    }

    private static ChordDto? Chord(InputChord? chord) => chord is { Key: not VirtualKey.None }
        ? new ChordDto(KeyCodes.ToCode(chord.Key), KeyCodes.FormatModifiers(chord.Modifiers))
        : null;

    private static XyDto? Xy(ScreenPoint? point) => point is { } at ? new XyDto(at.X, at.Y) : null;

    private static ScreenPoint? Point(XyDto? xy) => xy is { } at ? new ScreenPoint(at.X, at.Y) : null;

    private static InputChord? Chord(ChordDto? chord) => chord is not null && KeyCodes.TryGetVirtualKey(chord.Key, out var key)
        ? new InputChord(key, KeyCodes.ParseModifiers(chord.Mods))
        : null;

    private static List<ScriptBlock> FromDto(List<BlockDto> blocks, Dictionary<string, UnknownBlock> unknown) =>
        blocks.Where(b => b is not null).Select(b => FromDto(b, unknown)).ToList();

    private static ScriptBlock FromDto(BlockDto dto, Dictionary<string, UnknownBlock> unknown)
    {
        ScriptBlock block = dto.Type switch
        {
            "press" => FromDto(dto, WithInput(new PressBlock(), dto)),
            "hold" => WithInput(new HoldBlock(), dto),
            "release" => WithInput(new ReleaseBlock(), dto),
            "holdFor" => FromDto(dto, WithInput(new HoldForBlock(), dto)),
            "scroll" => FromDto(dto, new ScrollBlock()),
            "drag" => FromDto(dto, new DragBlock()),
            "type" => new TypeTextBlock { Text = dto.Text ?? string.Empty, CharDelayMs = dto.Ms ?? new TypeTextBlock().CharDelayMs },
            "move" => FromDto(dto, new MoveBlock()),
            "shift" => new ShiftBlock { Dx = dto.Dx ?? 0, Dy = dto.Dy ?? 0 },
            "wait" => FromDto(dto, new WaitBlock()),
            "waitKey" => new WaitKeyBlock { Input = Chord(dto.Input) },
            "repeat" => FromDto(dto, new RepeatBlock(), unknown),
            "random" => new RandomBlock { Blocks = dto.Blocks is { } inner ? FromDto(inner, unknown) : new() },
            "stop" => new StopBlock(),
            "label" => new LabelBlock { Text = dto.Text ?? string.Empty },
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
            ? new InputChord(key, KeyCodes.ParseModifiers(chord.Mods)) { PressMs = dto.Press ?? InputChord.DefaultPressMs }
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
        wait.InSeconds = dto.Unit == "s";
        return wait;
    }

    private static PressBlock FromDto(BlockDto dto, PressBlock press)
    {
        press.Click = dto.Click == "double" ? ClickType.Double : ClickType.Single;
        press.ProfilePress = dto.Dur == "profile";
        return press;
    }

    private static HoldForBlock FromDto(BlockDto dto, HoldForBlock holdFor)
    {
        holdFor.DurationMs = dto.Ms ?? holdFor.DurationMs;
        holdFor.RandomizeEnabled = dto.Rand ?? holdFor.RandomizeEnabled;
        holdFor.MinDurationMs = dto.MinMs ?? holdFor.MinDurationMs;
        holdFor.MaxDurationMs = dto.MaxMs ?? holdFor.MaxDurationMs;
        return holdFor;
    }

    private static ScrollBlock FromDto(BlockDto dto, ScrollBlock scroll)
    {
        scroll.Direction = dto.Dir == "up" ? ScrollDirection.Up : ScrollDirection.Down;
        scroll.Notches = dto.Count ?? scroll.Notches;
        scroll.At = Point(dto.At);
        return scroll;
    }

    private static DragBlock FromDto(BlockDto dto, DragBlock drag)
    {
        if (dto.Input is { } chord && KeyCodes.TryGetVirtualKey(chord.Key, out var key) && key.TryGetMouseButton(out var button))
        {
            drag.Button = button;
        }

        drag.From = Point(dto.At) ?? default;
        drag.To = Point(dto.To) ?? default;
        drag.Speed = dto.Speed switch
        {
            "slow" => DragSpeed.Slow,
            "fast" => DragSpeed.Fast,
            _ => DragSpeed.Medium,
        };
        return drag;
    }

    private static MoveBlock FromDto(BlockDto dto, MoveBlock move)
    {
        move.To = Point(dto.To) ?? default;
        move.Movement = dto.How switch
        {
            "instant" => MovementMode.Instant,
            "smooth" => MovementMode.Smooth,
            _ => null,
        };
        move.DurationMs = dto.Ms ?? move.DurationMs;
        return move;
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
