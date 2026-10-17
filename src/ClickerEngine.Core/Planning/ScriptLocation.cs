namespace ClickerEngine.Core.Planning;

/// <summary>How far one enclosing «Повторить» has got.</summary>
/// <param name="BlockId">The repeat block.</param>
/// <param name="Iteration">The current iteration, from 1.</param>
/// <param name="Count">Iterations in all, or null for an endless repeat.</param>
public readonly record struct LoopProgress(string BlockId, long Iteration, long? Count)
{
    public override string ToString() => Count is { } count
        ? $"{BlockId} {Iteration}/{count}"
        : $"{BlockId} {Iteration}/∞";
}

/// <summary>
/// Where in a script a step comes from: which block, in which pass of the script, and in
/// which iteration of every «Повторить» around it. The engine reports it as the block being
/// executed (<see cref="Engine.AutoClickerEngine.BlockStarted"/>), so an interface can
/// highlight that block and show the counters.
/// <para>
/// The planner creates one per execution of a block, so two consecutive runs of the same
/// block (inside a repeat, say) are two different locations.
/// </para>
/// </summary>
public sealed class ScriptLocation
{
    public ScriptLocation(string blockId, long pass, long? passes, IReadOnlyList<LoopProgress> loops)
    {
        BlockId = blockId ?? string.Empty;
        Pass = pass;
        Passes = passes;
        Loops = loops ?? Array.Empty<LoopProgress>();
    }

    /// <summary>The id of the block being executed.</summary>
    public string BlockId { get; }

    /// <summary>The current pass of the whole script, from 1.</summary>
    public long Pass { get; }

    /// <summary>Passes in all, or null when the script repeats until stopped.</summary>
    public long? Passes { get; }

    /// <summary>The repeats around the block, outermost first.</summary>
    public IReadOnlyList<LoopProgress> Loops { get; }

    public override string ToString()
    {
        var pass = Passes is { } passes ? $"{Pass}/{passes}" : $"{Pass}/∞";
        return Loops.Count == 0
            ? $"{BlockId} (pass {pass})"
            : $"{BlockId} (pass {pass}, {string.Join(", ", Loops)})";
    }
}
