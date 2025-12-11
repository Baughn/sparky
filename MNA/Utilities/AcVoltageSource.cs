using System;
using Sparky.MNA.Api;

namespace Sparky.MNA.Utilities;

/// <summary>
/// AC voltage source: V(t) = Offset + Amplitude × sin(2π × Frequency × t + Phase)
/// </summary>
public class AcVoltageSource : TimeVaryingSource
{
    /// <summary>The underlying voltage source ID.</summary>
    public VoltageSourceId Id { get; }

    /// <summary>Peak amplitude in volts.</summary>
    public double Amplitude { get; set; }

    /// <summary>Frequency in Hz.</summary>
    public double Frequency { get; set; }

    /// <summary>Phase offset in radians.</summary>
    public double Phase { get; set; }

    /// <summary>DC offset in volts.</summary>
    public double Offset { get; set; }

    /// <summary>
    /// Creates an AC voltage source and adds it to the simulation.
    /// </summary>
    /// <param name="sim">The simulation to add the source to.</param>
    /// <param name="nodePos">Positive terminal node.</param>
    /// <param name="nodeNeg">Negative terminal node.</param>
    /// <param name="amplitude">Peak amplitude in volts.</param>
    /// <param name="frequency">Frequency in Hz.</param>
    /// <param name="phase">Phase offset in radians (default 0).</param>
    /// <param name="offset">DC offset in volts (default 0).</param>
    public AcVoltageSource(
        ISimulation sim,
        NodeId nodePos,
        NodeId nodeNeg,
        double amplitude,
        double frequency,
        double phase = 0,
        double offset = 0
    )
        : base(sim)
    {
        Amplitude = amplitude;
        Frequency = frequency;
        Phase = phase;
        Offset = offset;

        // Create with initial value at t=0
        double initialValue = GetValue(0);
        Id = sim.AddVoltageSource(nodePos, nodeNeg, initialValue);
    }

    /// <inheritdoc />
    public override double GetValue(double time)
    {
        return Offset + Amplitude * Math.Sin(2 * Math.PI * Frequency * time + Phase);
    }

    /// <inheritdoc />
    public override void Update()
    {
        double value = GetValue(Sim.SimulationTime);
        Sim.UpdateVoltageSource(Id, value);
    }

    /// <inheritdoc />
    public override void Remove()
    {
        Sim.RemoveVoltageSource(Id);
    }

    /// <inheritdoc />
    public override bool Exists => Sim.VoltageSourceExists(Id);
}
