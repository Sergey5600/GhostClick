using System.Diagnostics;
using System.Runtime.Versioning;
using ClickerEngine.Core.Native;

namespace ClickerEngine.Core.Timing;

/// <summary>
/// The engine's only source of time. Injecting it keeps the run loop deterministic and
/// instant under test.
/// </summary>
public interface IClock
{
    /// <summary>Monotonic time since the clock was created.</summary>
    TimeSpan Elapsed { get; }

    /// <summary>
    /// Blocks for <paramref name="duration"/>, returning early if
    /// <paramref name="cancellationToken"/> is signalled. Durations of zero or less return
    /// immediately.
    /// </summary>
    void Wait(TimeSpan duration, CancellationToken cancellationToken);
}

/// <summary>
/// A wall-clock implementation accurate to about a millisecond: it sleeps for the bulk of
/// the wait (so the thread is not burning CPU) and spins for the last stretch, because
/// <c>Thread.Sleep</c> alone only resolves to the system timer tick.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>Below this many milliseconds left, switch from sleeping to spinning.</summary>
    private static readonly TimeSpan SpinThreshold = TimeSpan.FromMilliseconds(2);

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void Wait(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            // Still honour cancellation so a tight zero-delay loop stays responsive.
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        long target = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = TimeSpan.FromSeconds(
                (target - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency);

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            if (remaining > SpinThreshold)
            {
                // WaitHandle.WaitOne wakes immediately on cancellation, unlike Thread.Sleep.
                var coarse = remaining - SpinThreshold;
                if (cancellationToken.CanBeCanceled)
                {
                    if (cancellationToken.WaitHandle.WaitOne(coarse))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                else
                {
                    Thread.Sleep(coarse);
                }
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
    }
}

/// <summary>
/// Raises the Windows timer resolution to 1 ms for its lifetime, so sleeps land close to
/// where they were asked to. No-op on non-Windows and harmless if the call fails.
/// </summary>
public sealed class HighResolutionTimerScope : IDisposable
{
    private readonly bool _acquired;
    private bool _disposed;

    public HighResolutionTimerScope()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _acquired = BeginPeriod();
    }

    public void Dispose()
    {
        if (_disposed || !_acquired || !OperatingSystem.IsWindows())
        {
            _disposed = true;
            return;
        }

        _disposed = true;
        EndPeriod();
    }

    [SupportedOSPlatform("windows")]
    private static bool BeginPeriod()
    {
        try
        {
            return NativeMethods.TimeBeginPeriod(1) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void EndPeriod()
    {
        try
        {
            NativeMethods.TimeEndPeriod(1);
        }
        catch (DllNotFoundException)
        {
            // Nothing to release.
        }
        catch (EntryPointNotFoundException)
        {
            // Nothing to release.
        }
    }
}

/// <summary>
/// A clock that never really sleeps: <see cref="Wait"/> just advances virtual time and
/// records what it was asked to wait for. Used by the tests to drive whole runs instantly.
/// </summary>
public sealed class VirtualClock : IClock
{
    private readonly List<TimeSpan> _waits = new();

    public TimeSpan Elapsed { get; private set; }

    /// <summary>Every duration passed to <see cref="Wait"/>, in order.</summary>
    public IReadOnlyList<TimeSpan> Waits => _waits;

    /// <summary>Optional hook invoked before each wait, to simulate external events.</summary>
    public Action<VirtualClock>? OnWait { get; set; }

    public void Wait(TimeSpan duration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _waits.Add(duration);

        if (duration > TimeSpan.Zero)
        {
            Elapsed += duration;
        }

        OnWait?.Invoke(this);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Moves virtual time forward without recording a wait.</summary>
    public void Advance(TimeSpan amount) => Elapsed += amount;
}
