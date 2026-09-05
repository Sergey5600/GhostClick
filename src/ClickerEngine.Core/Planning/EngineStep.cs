using ClickerEngine.Core.Configuration;

namespace ClickerEngine.Core.Planning;

/// <summary>The primitive operations a plan is made of.</summary>
public enum EngineStepKind
{
    /// <summary>Block for <see cref="EngineStep.Delay"/>.</summary>
    Wait,

    /// <summary>Place the cursor at <see cref="EngineStep.Position"/>.</summary>
    MoveTo,

    /// <summary>Press <see cref="EngineStep.Button"/>.</summary>
    MouseDown,

    /// <summary>Release <see cref="EngineStep.Button"/>.</summary>
    MouseUp,

    /// <summary>Press <see cref="EngineStep.Key"/>.</summary>
    KeyDown,

    /// <summary>Release <see cref="EngineStep.Key"/>.</summary>
    KeyUp,
}

/// <summary>
/// A single instruction produced by <see cref="ActionPlanner"/> and executed by the engine.
/// <para>
/// Keeping the plan as a stream of these primitives is what makes the whole engine testable:
/// a test can enumerate a plan and assert on the exact sequence of moves, presses and waits
/// without any clock, any thread, or any Windows API.
/// </para>
/// </summary>
public readonly record struct EngineStep
{
    private EngineStep(EngineStepKind kind)
    {
        Kind = kind;
    }

    public EngineStepKind Kind { get; private init; }

    /// <summary>Duration for <see cref="EngineStepKind.Wait"/> steps.</summary>
    public TimeSpan Delay { get; private init; }

    /// <summary>Target for <see cref="EngineStepKind.MoveTo"/> steps.</summary>
    public ScreenPoint Position { get; private init; }

    /// <summary>Button for mouse steps.</summary>
    public MouseButton Button { get; private init; }

    /// <summary>Key for keyboard steps.</summary>
    public VirtualKey Key { get; private init; }

    /// <summary>
    /// True for the modifier presses that wrap a key action, so the engine does not count
    /// them as key presses in the session statistics.
    /// </summary>
    public bool IsModifier { get; private init; }

    /// <summary>
    /// True on the release step that completes one logical action, so the engine knows when
    /// to increment its counters and raise <c>ActionExecuted</c>.
    /// </summary>
    public bool CompletesAction { get; private init; }

    public static EngineStep Wait(TimeSpan delay) =>
        new(EngineStepKind.Wait) { Delay = delay };

    public static EngineStep Wait(int milliseconds) =>
        Wait(TimeSpan.FromMilliseconds(milliseconds));

    public static EngineStep MoveTo(ScreenPoint position) =>
        new(EngineStepKind.MoveTo) { Position = position };

    public static EngineStep MouseDown(MouseButton button) =>
        new(EngineStepKind.MouseDown) { Button = button };

    public static EngineStep MouseUp(MouseButton button, bool completesAction = true) =>
        new(EngineStepKind.MouseUp) { Button = button, CompletesAction = completesAction };

    public static EngineStep KeyDown(VirtualKey key, bool isModifier = false) =>
        new(EngineStepKind.KeyDown) { Key = key, IsModifier = isModifier };

    public static EngineStep KeyUp(VirtualKey key, bool isModifier = false, bool completesAction = true) =>
        new(EngineStepKind.KeyUp)
        {
            Key = key,
            IsModifier = isModifier,
            CompletesAction = completesAction && !isModifier,
        };

    public override string ToString() => Kind switch
    {
        EngineStepKind.Wait => $"Wait {Delay.TotalMilliseconds:0.##} ms",
        EngineStepKind.MoveTo => $"MoveTo {Position}",
        EngineStepKind.MouseDown => $"MouseDown {Button}",
        EngineStepKind.MouseUp => $"MouseUp {Button}",
        EngineStepKind.KeyDown => $"KeyDown {Key}",
        _ => $"KeyUp {Key}",
    };
}
