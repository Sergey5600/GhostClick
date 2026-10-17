using System.Globalization;
using ClickerEngine.Core.Configuration;

namespace GhostClick.Bridge;

/// <summary>
/// One problem with the current profile, in the form the page marks blocks with (see
/// docs/scripts-ui-contract.md, «Ошибки»).
/// </summary>
public sealed class IssueDto
{
    /// <summary>The id of the block at fault, or null when the problem is not in a block.</summary>
    public string? Block { get; set; }

    /// <summary>
    /// Where the block is: indexes into <c>script.blocks</c>, then into each repeat's
    /// <c>blocks</c>. Tells apart two blocks that share an id; null outside the blocks.
    /// </summary>
    public int[]? Path { get; set; }

    /// <summary>The field at fault, by its name in the page's data (<c>count</c>, <c>maxMs</c>...), or null.</summary>
    public string? Field { get; set; }

    public string Message { get; set; } = string.Empty;
}

/// <summary>The interface's words for scripts: block names and validation messages.</summary>
public static class ScriptTexts
{
    private const string Prefix = "Script.";

    /// <summary>The name of a block type as the interface shows it.</summary>
    public static string Title(string? type) => type switch
    {
        "press" => "Нажать",
        "hold" => "Зажать",
        "release" => "Отпустить",
        "move" => "Переместить курсор",
        "wait" => "Ждать",
        "repeat" => "Повторить",
        "stop" => "Стоп",
        _ => "Неизвестный блок",
    };

    /// <summary>
    /// The problem as an issue for the page. <paramref name="message"/> is the text to show; a
    /// problem in a block carries the block's id, index path and field.
    /// </summary>
    public static IssueDto Issue(ValidationError error, string message)
    {
        var issue = new IssueDto { Message = message };
        if (!TryParse(error.Path, out var path, out var field))
        {
            return issue;
        }

        issue.Block = error.BlockId;
        issue.Path = path.Length > 0 ? path : null;
        issue.Field = field is null ? null : PageField(field, inBlock: path.Length > 0);
        return issue;
    }

    /// <summary>The interface's message for a problem in the script, or null for any other problem.</summary>
    public static string? Message(ValidationError error, ScriptSettings? script)
    {
        if (!TryParse(error.Path, out var path, out var field))
        {
            return null;
        }

        if (path.Length == 0)
        {
            return field switch
            {
                nameof(ScriptSettings.Blocks) when script?.Blocks is not { Count: > 0 } => "Добавьте в сценарий хотя бы один блок",
                nameof(ScriptSettings.Blocks) => "Все блоки сценария выключены",
                nameof(ScriptSettings.RunCount) => "Количество прогонов должно быть не меньше 1",
                nameof(ScriptSettings.StepDelayMs) => "Пауза между шагами не может быть отрицательной",
                _ => error.Message,
            };
        }

        return field switch
        {
            null => "Пустой блок",
            nameof(ScriptBlock.Id) when error.BlockId is null => "У блока нет id",
            nameof(ScriptBlock.Id) => "Такой id уже есть у другого блока",
            nameof(ScriptBlock.Type) when script?.Find(error.BlockId) is UnknownBlock { TypeName.Length: 0 } => "У блока не указан тип",
            nameof(ScriptBlock.Type) => "Этот блок не поддерживается этой версией GhostClick",
            nameof(InputBlock.Input) => "Выберите клавишу или кнопку мыши",
            nameof(WaitBlock.DurationMs) => "Время ожидания не может быть отрицательным",
            nameof(WaitBlock.MinDurationMs) => "Минимальное время ожидания не может быть отрицательным",
            nameof(WaitBlock.MaxDurationMs) => "Максимальное время ожидания меньше минимального",
            nameof(RepeatBlock.Count) => "Количество повторов должно быть не меньше 1",
            nameof(RepeatBlock.Blocks) => "Внутри повтора нет включённых блоков",
            _ => error.Message,
        };
    }

    /// <summary>
    /// A problem in the script as a one-line notice, naming the block it is in - by its label,
    /// or by its type when it has none: «Повторить»: Количество повторов…
    /// </summary>
    public static string? Describe(ValidationError error, ScriptSettings? script)
    {
        if (Message(error, script) is not { } message)
        {
            return null;
        }

        var block = script?.Find(error.BlockId);
        if (block is null)
        {
            return message;
        }

        var name = string.IsNullOrWhiteSpace(block.Label) ? Title(block.Type) : block.Label.Trim();
        return $"«{name}»: {message}";
    }

    /// <summary>
    /// Splits <c>Script.Blocks[1].Blocks[0].Count</c> into <c>[1, 0]</c> and <c>Count</c>.
    /// False for a path outside the script.
    /// </summary>
    private static bool TryParse(string? errorPath, out int[] path, out string? field)
    {
        path = Array.Empty<int>();
        field = null;

        if (errorPath is null || !errorPath.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var indexes = new List<int>();
        foreach (var segment in errorPath[Prefix.Length..].Split('.'))
        {
            int open = segment.IndexOf('[');
            if (open < 0)
            {
                field = segment;
                continue;
            }

            field = null;
            if (int.TryParse(segment.AsSpan(open + 1, segment.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                indexes.Add(index);
            }
        }

        path = indexes.ToArray();
        return true;
    }

    /// <summary>A field's name in the page's data (<see cref="ScriptDto"/>, <see cref="BlockDto"/>).</summary>
    private static string PageField(string field, bool inBlock) => (field, inBlock) switch
    {
        (nameof(ScriptSettings.RunCount), false) => "count",
        (nameof(ScriptSettings.StepDelayMs), false) => "stepDelay",
        (nameof(WaitBlock.DurationMs), true) => "ms",
        (nameof(WaitBlock.MinDurationMs), true) => "minMs",
        (nameof(WaitBlock.MaxDurationMs), true) => "maxMs",
        _ => char.ToLowerInvariant(field[0]) + field[1..],
    };
}
