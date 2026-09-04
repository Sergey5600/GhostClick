namespace ClickerEngine.Core.Engine;

/// <summary>
/// Counters for the whole session (all runs since the process started or since the last
/// <see cref="Reset"/>). Safe to read from a UI thread while the engine thread writes.
/// </summary>
public sealed class SessionStatistics
{
    private long _mouseClicks;
    private long _keyPresses;
    private long _actions;
    private long _runs;
    private long _totalRunTicks;

    /// <summary>Physical mouse button releases sent. A double click counts as two.</summary>
    public long MouseClicks => Interlocked.Read(ref _mouseClicks);

    /// <summary>Key releases sent, not counting the modifiers wrapped around them.</summary>
    public long KeyPresses => Interlocked.Read(ref _keyPresses);

    /// <summary>Logical actions completed. A double click counts as one.</summary>
    public long Actions => Interlocked.Read(ref _actions);

    /// <summary>Runs started since the last reset.</summary>
    public long Runs => Interlocked.Read(ref _runs);

    /// <summary>Total time spent running.</summary>
    public TimeSpan TotalRunTime => TimeSpan.FromTicks(Interlocked.Read(ref _totalRunTicks));

    internal void RecordMouseClick() => Interlocked.Increment(ref _mouseClicks);

    internal void RecordKeyPress() => Interlocked.Increment(ref _keyPresses);

    internal void RecordAction() => Interlocked.Increment(ref _actions);

    internal void RecordRunStarted() => Interlocked.Increment(ref _runs);

    internal void RecordRunTime(TimeSpan duration) =>
        Interlocked.Add(ref _totalRunTicks, Math.Max(0, duration.Ticks));

    /// <summary>Zeroes every counter.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _mouseClicks, 0);
        Interlocked.Exchange(ref _keyPresses, 0);
        Interlocked.Exchange(ref _actions, 0);
        Interlocked.Exchange(ref _runs, 0);
        Interlocked.Exchange(ref _totalRunTicks, 0);
    }

    public override string ToString() =>
        $"{Actions} actions, {MouseClicks} clicks, {KeyPresses} key presses, {Runs} runs, {TotalRunTime:hh\\:mm\\:ss}";
}
