using System.Text.Json.Serialization;

namespace ClickerEngine.Core.Configuration;

/// <summary>
/// The blocks a <see cref="ProfileKind.Script"/> profile runs, top to bottom, and how often.
/// <para>
/// A script uses the rest of its profile like a clicker does: hotkeys, the emergency stop and
/// the safety limits, the movement settings for cursor travel, jitter for points and waits,
/// and the hold time for «Нажать».
/// </para>
/// </summary>
public sealed class ScriptSettings
{
    /// <summary>Once, a fixed number of times, or until stopped.</summary>
    public ScriptRunMode Runs { get; set; } = ScriptRunMode.Once;

    /// <summary>Passes when <see cref="Runs"/> is <see cref="ScriptRunMode.FixedCount"/>.</summary>
    public int RunCount { get; set; } = 10;

    /// <summary>
    /// The automatic pause between two steps that have no «Ждать» between them, in
    /// milliseconds. A «Ждать» replaces it, so «Ждать 0 мс» runs two steps back to back.
    /// </summary>
    public int StepDelayMs { get; set; } = 100;

    public List<ScriptBlock> Blocks { get; set; } = new();

    /// <summary>
    /// Not saved: when set, the run starts at this block («Запустить отсюда») and everything
    /// before it in the first pass is skipped. The interface's test run sets it on a copy.
    /// </summary>
    [JsonIgnore]
    public string? StartBlockId { get; set; }

    /// <summary>
    /// True when the script can run until it is stopped: it repeats forever, or an enabled
    /// «Повторить» inside it does.
    /// </summary>
    [JsonIgnore]
    public bool IsEndless =>
        Runs == ScriptRunMode.Infinite ||
        Walk(Blocks).Any(b => b is RepeatBlock { Mode: RepeatMode.Infinite });

    /// <summary>A deep copy.</summary>
    public ScriptSettings Clone()
    {
        var clone = (ScriptSettings)MemberwiseClone();
        clone.Blocks = CloneBlocks(Blocks);
        return clone;
    }

    /// <summary>
    /// Every block, depth first in the order they run. With <paramref name="enabledOnly"/>,
    /// switched-off blocks and everything inside them are left out.
    /// </summary>
    public static IEnumerable<ScriptBlock> Walk(IEnumerable<ScriptBlock>? blocks, bool enabledOnly = true)
    {
        foreach (var block in blocks ?? Enumerable.Empty<ScriptBlock>())
        {
            if (block is null || (enabledOnly && !block.Enabled))
            {
                continue;
            }

            yield return block;

            if (block is IBlockContainer container)
            {
                foreach (var inner in Walk(container.Blocks, enabledOnly))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>The block with this id anywhere in the script, or null.</summary>
    public ScriptBlock? Find(string? id) =>
        id is null ? null : Walk(Blocks, enabledOnly: false).FirstOrDefault(b => b.Id == id);

    /// <summary>
    /// Deep-copies a block list. A hand-edited file can hold <c>"blocks": null</c> or null
    /// entries; they are normalised here so the validator, not a NullReferenceException,
    /// reports what is wrong.
    /// </summary>
    internal static List<ScriptBlock> CloneBlocks(List<ScriptBlock>? blocks) =>
        blocks?.Where(b => b is not null).Select(b => b.Clone()).ToList() ?? new();
}
