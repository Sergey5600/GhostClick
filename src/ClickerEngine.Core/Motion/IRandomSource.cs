namespace ClickerEngine.Core.Motion;

/// <summary>
/// The engine's only source of randomness, so jitter and interval spread can be replayed
/// deterministically in tests.
/// </summary>
public interface IRandomSource
{
    /// <summary>A value in [minInclusive, maxInclusive].</summary>
    int NextInt(int minInclusive, int maxInclusive);

    /// <summary>A value in [0, 1).</summary>
    double NextDouble();
}

/// <summary>Backed by <see cref="Random"/>.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;

    public SystemRandomSource()
        : this(Random.Shared)
    {
    }

    public SystemRandomSource(int seed)
        : this(new Random(seed))
    {
    }

    public SystemRandomSource(Random random) => _random = random ?? throw new ArgumentNullException(nameof(random));

    public int NextInt(int minInclusive, int maxInclusive) => maxInclusive <= minInclusive
        ? minInclusive
        : _random.Next(minInclusive, maxInclusive + 1);

    public double NextDouble() => _random.NextDouble();
}

/// <summary>
/// Returns a fixed sequence, looping when exhausted. Makes assertions about jitter exact
/// rather than statistical.
/// </summary>
public sealed class ScriptedRandomSource : IRandomSource
{
    private readonly IReadOnlyList<double> _values;
    private int _index;

    /// <param name="values">Values in [0, 1), replayed in order and then looped.</param>
    public ScriptedRandomSource(params double[] values)
    {
        if (values is null || values.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        _values = values;
    }

    public int NextInt(int minInclusive, int maxInclusive)
    {
        if (maxInclusive <= minInclusive)
        {
            return minInclusive;
        }

        long span = (long)maxInclusive - minInclusive + 1;
        long offset = (long)(NextDouble() * span);
        return (int)(minInclusive + Math.Min(offset, span - 1));
    }

    public double NextDouble()
    {
        double value = _values[_index];
        _index = (_index + 1) % _values.Count;
        return value;
    }
}

/// <summary>Always returns the midpoint, i.e. no jitter at all.</summary>
public sealed class NeutralRandomSource : IRandomSource
{
    public static readonly NeutralRandomSource Instance = new();

    public int NextInt(int minInclusive, int maxInclusive) =>
        maxInclusive <= minInclusive ? minInclusive : minInclusive + ((maxInclusive - minInclusive) / 2);

    public double NextDouble() => 0.5;
}
