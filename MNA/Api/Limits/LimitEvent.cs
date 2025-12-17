namespace Sparky.MNA.Api.Limits;

/// <summary>
/// Event data fired when a component limit is exceeded or cleared.
/// </summary>
public readonly record struct LimitEvent {
    /// <summary>The component that triggered the event.</summary>
    public required ComponentRef Component { get; init; }

    /// <summary>The type of limit that was exceeded or cleared.</summary>
    public required LimitKind Kind { get; init; }

    /// <summary>The configured threshold value.</summary>
    public required double Threshold { get; init; }

    /// <summary>The actual measured value that triggered the event.</summary>
    public required double ActualValue { get; init; }

    /// <summary>
    /// True if the limit was just exceeded (rising edge).
    /// False if the limit was just cleared (falling edge).
    /// </summary>
    public required bool IsExceeded { get; init; }

    /// <summary>Cumulative simulation time when the event occurred.</summary>
    public double SimulationTime { get; init; }
}

/// <summary>
/// Delegate for handling limit events.
/// </summary>
public delegate void LimitEventHandler(LimitEvent evt);
