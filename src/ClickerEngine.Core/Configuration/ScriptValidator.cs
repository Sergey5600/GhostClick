namespace ClickerEngine.Core.Configuration;

/// <summary>
/// The script half of <see cref="ProfileValidator"/>. A problem inside a block carries the
/// block's id (<see cref="ValidationError.BlockId"/>) and a path ending in the field at fault,
/// e.g. <c>Script.Blocks[1].Blocks[0].Count</c>, so an interface can mark the very block and
/// field to fix.
/// <para>
/// Switched-off blocks are not checked - turning a half-finished block off is how a user
/// parks it - except for their ids, which the interface needs for every block.
/// </para>
/// </summary>
internal static class ScriptValidator
{
    private const string Root = "Script";

    public static void Validate(ScriptSettings? script, List<ValidationError> errors)
    {
        if (script?.Blocks is null || !script.Blocks.Any(b => b is not null))
        {
            errors.Add(new ValidationError($"{Root}.Blocks", "A script needs at least one block."));
        }
        else if (!ScriptSettings.Walk(script.Blocks).Any())
        {
            errors.Add(new ValidationError($"{Root}.Blocks", "Every block of the script is switched off."));
        }

        if (script is null)
        {
            return;
        }

        if (script.Runs == ScriptRunMode.FixedCount && script.RunCount < 1)
        {
            errors.Add(new ValidationError($"{Root}.RunCount", "Run count must be at least 1."));
        }

        if (script.StepDelayMs < 0)
        {
            errors.Add(new ValidationError($"{Root}.StepDelayMs", "Step delay must not be negative."));
        }

        ValidateBlocks(script.Blocks, $"{Root}.Blocks", enabled: true, new HashSet<string>(StringComparer.Ordinal), new List<InputChord>(), errors);
    }

    private const string HeldField = ReleaseBlock.HeldField;

    /// <summary>
    /// A «Отпустить» must release something a «Зажать» above it (in list order, switched-off
    /// blocks left out) is still holding. The order is the order on screen, not of a run: a
    /// repeat is read once, top to bottom.
    /// </summary>
    private static void TrackHeld(ScriptBlock block, string at, List<InputChord> held, List<ValidationError> errors)
    {
        switch (block)
        {
            case HoldBlock { Input: { Key: not VirtualKey.None } input }:
                held.Add(input);
                break;

            case ReleaseBlock { Input: { Key: not VirtualKey.None } input }:
                int index = held.FindIndex(h => h.Key == input.Key && h.Modifiers == input.Modifiers);
                if (index >= 0)
                {
                    held.RemoveAt(index);
                    break;
                }

                errors.Add(new ValidationError(
                    $"{at}.{HeldField}",
                    held.Count == 0 ? "Nothing is held above this release." : $"{input} is not held above this release.")
                {
                    BlockId = string.IsNullOrWhiteSpace(block.Id) ? null : block.Id,
                });
                break;
        }
    }

    private static void ValidateBlocks(
        List<ScriptBlock>? blocks,
        string path,
        bool enabled,
        HashSet<string> ids,
        List<InputChord> held,
        List<ValidationError> errors)
    {
        if (blocks is null)
        {
            return;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            var at = $"{path}[{i}]";
            var block = blocks[i];
            if (block is null)
            {
                errors.Add(new ValidationError(at, "A block must not be null."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(block.Id))
            {
                errors.Add(new ValidationError($"{at}.Id", "Every block needs an id."));
            }
            else if (!ids.Add(block.Id))
            {
                errors.Add(new ValidationError($"{at}.Id", $"Block id '{block.Id}' is used by another block.") { BlockId = block.Id });
            }

            bool live = enabled && block.Enabled;
            if (live)
            {
                ValidateBlock(block, at, errors);
                TrackHeld(block, at, held, errors);
            }

            if (block is IBlockContainer container)
            {
                ValidateBlocks(container.Blocks, $"{at}.Blocks", live, ids, held, errors);
            }
        }
    }

    private static void ValidateBlock(ScriptBlock block, string at, List<ValidationError> errors)
    {
        void Add(string field, string message) => errors.Add(new ValidationError($"{at}.{field}", message)
        {
            BlockId = string.IsNullOrWhiteSpace(block.Id) ? null : block.Id,
        });

        switch (block)
        {
            case HoldForBlock holdFor:
                if (holdFor.Input is null || holdFor.Input.Key == VirtualKey.None)
                {
                    Add(nameof(HoldForBlock.Input), "Choose a key or mouse button.");
                }

                ValidateDuration(holdFor.RandomizeEnabled, holdFor.DurationMs, holdFor.MinDurationMs, holdFor.MaxDurationMs, Add);
                break;

            case InputBlock input:
                if (input.Input is null || input.Input.Key == VirtualKey.None)
                {
                    Add(nameof(InputBlock.Input), "Choose a key or mouse button.");
                }
                else if (block is PressBlock && input.Input.PressMs < 0)
                {
                    Add($"{nameof(InputBlock.Input)}.{nameof(InputChord.PressMs)}", "Press duration must not be negative.");
                }

                break;

            case ScrollBlock scroll:
                if (scroll.Notches < 1)
                {
                    Add(nameof(ScrollBlock.Notches), "Scroll by at least one notch.");
                }

                break;

            case TypeTextBlock type:
                if (string.IsNullOrEmpty(type.Text))
                {
                    Add(nameof(TypeTextBlock.Text), "There is no text to type.");
                }

                if (type.CharDelayMs < 0)
                {
                    Add(nameof(TypeTextBlock.CharDelayMs), "The pause between characters must not be negative.");
                }

                break;

            case MoveBlock move:
                if (move.DurationMs < 0)
                {
                    Add(nameof(MoveBlock.DurationMs), "Travel time must not be negative.");
                }

                break;

            case WaitKeyBlock waitKey:
                if (waitKey.Input is null || waitKey.Input.Key == VirtualKey.None)
                {
                    Add(nameof(WaitKeyBlock.Input), "Choose a key.");
                }

                break;

            case RandomBlock random:
                if (!ScriptSettings.Walk(random.Blocks).Any())
                {
                    Add(nameof(RandomBlock.Blocks), "Pick from at least one enabled block inside.");
                }

                break;

            case WaitBlock wait when !wait.RandomizeEnabled:
                if (wait.DurationMs < 0)
                {
                    Add(nameof(WaitBlock.DurationMs), "Wait must not be negative.");
                }

                break;

            case WaitBlock wait:
                if (wait.MinDurationMs < 0)
                {
                    Add(nameof(WaitBlock.MinDurationMs), "Minimum wait must not be negative.");
                }

                if (wait.MaxDurationMs < wait.MinDurationMs)
                {
                    Add(
                        nameof(WaitBlock.MaxDurationMs),
                        $"Maximum wait ({wait.MaxDurationMs} ms) must be >= minimum wait ({wait.MinDurationMs} ms).");
                }

                break;

            case RepeatBlock repeat:
                if (repeat.Mode == RepeatMode.FixedCount && repeat.Count < 1)
                {
                    Add(nameof(RepeatBlock.Count), "Repeat count must be at least 1.");
                }

                // Also what keeps an endless repeat from spinning without ever giving the
                // engine a step at which it could stop.
                if (!ScriptSettings.Walk(repeat.Blocks).Any())
                {
                    Add(nameof(RepeatBlock.Blocks), "A repeat needs at least one enabled block inside.");
                }

                break;

            case UnknownBlock unknown:
                Add(
                    nameof(ScriptBlock.Type),
                    unknown.TypeName.Length == 0
                        ? "The block has no type."
                        : $"Unknown block type '{unknown.TypeName}'; it may come from a newer version.");
                break;
        }
    }

    private static void ValidateDuration(bool random, int duration, int min, int max, Action<string, string> add)
    {
        if (!random && duration < 0)
        {
            add("DurationMs", "Wait must not be negative.");
        }

        if (random && min < 0)
        {
            add("MinDurationMs", "Minimum wait must not be negative.");
        }

        if (random && max < min)
        {
            add("MaxDurationMs", $"Maximum wait ({max} ms) must be >= minimum wait ({min} ms).");
        }
    }
}
