namespace Sparky.MNA.Api.Limits;

/// <summary>
/// Configuration for a component limit.
/// </summary>
public readonly record struct LimitConfig {
    /// <summary>
    /// The threshold value that triggers the limit.
    /// Comparison is signed (value > threshold triggers).
    /// </summary>
    public required double Threshold { get; init; }

    /// <summary>
    /// Hysteresis value for clearing the limit.
    /// The event clears when value drops below (Threshold - Hysteresis).
    /// Default is 0, meaning the event clears as soon as value is below threshold.
    /// </summary>
    public double Hysteresis { get; init; }

    /// <summary>
    /// If true, the callback fires every step while the limit is exceeded.
    /// If false (default), the callback fires only on the rising edge (transition to exceeded).
    /// </summary>
    public bool FireEveryStep { get; init; }
}
