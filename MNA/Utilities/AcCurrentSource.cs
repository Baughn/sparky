using System;
using Sparky.MNA.Api;

namespace Sparky.MNA.Utilities;

/// <summary>
/// AC current source: I(t) = Offset + Amplitude × sin(2π × Frequency × t + Phase)
/// </summary>
public class AcCurrentSource : TimeVaryingSource {
    /// <summary>The underlying current source ID.</summary>
    public CurrentSourceId Id { get; }

    /// <summary>Peak amplitude in amperes.</summary>
    public double Amplitude { get; set; }

    /// <summary>Frequency in Hz.</summary>
    public double Frequency { get; set; }

    /// <summary>Phase offset in radians.</summary>
    public double Phase { get; set; }

    /// <summary>DC offset in amperes.</summary>
    public double Offset { get; set; }

    /// <summary>
    /// Creates an AC current source and adds it to the simulation.
    /// </summary>
    /// <param name="sim">The simulation to add the source to.</param>
    /// <param name="nodeIn">Node where current enters.</param>
    /// <param name="nodeOut">Node where current exits.</param>
    /// <param name="amplitude">Peak amplitude in amperes.</param>
    /// <param name="frequency">Frequency in Hz.</param>
    /// <param name="phase">Phase offset in radians (default 0).</param>
    /// <param name="offset">DC offset in amperes (default 0).</param>
    public AcCurrentSource(
        ISimulation sim,
        NodeId nodeIn,
        NodeId nodeOut,
        double amplitude,
        double frequency,
        double phase = 0,
        double offset = 0
    )
        : base(sim) {
        Amplitude = amplitude;
        Frequency = frequency;
        Phase = phase;
        Offset = offset;

        // Create with initial value at t=0
        double initialValue = GetValue(0);
        Id = sim.AddCurrentSource(nodeIn, nodeOut, initialValue);
    }

    /// <inheritdoc />
    public override double GetValue(double time) {
        return Offset + Amplitude * Math.Sin(2 * Math.PI * Frequency * time + Phase);
    }

    /// <inheritdoc />
    public override void Update() {
        double value = GetValue(Sim.SimulationTime);
        Sim.UpdateCurrentSource(Id, value);
    }

    /// <inheritdoc />
    public override void Remove() {
        Sim.RemoveCurrentSource(Id);
    }

    /// <inheritdoc />
    public override bool Exists => Sim.CurrentSourceExists(Id);
}
