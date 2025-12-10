using Sparky.MNA.Api;

namespace Sparky.MNA.Utilities;

/// <summary>
/// PWM voltage source: V = VHigh when (t % period) &lt; DutyCycle × period, else VLow.
/// </summary>
public class PwmVoltageSource : TimeVaryingSource
{
    /// <summary>The underlying voltage source ID.</summary>
    public VoltageSourceId Id { get; }

    /// <summary>Voltage during the "on" phase.</summary>
    public double VHigh { get; set; }

    /// <summary>Voltage during the "off" phase.</summary>
    public double VLow { get; set; }

    /// <summary>Frequency in Hz.</summary>
    public double Frequency { get; set; }

    /// <summary>Duty cycle from 0.0 to 1.0 (fraction of period at VHigh).</summary>
    public double DutyCycle { get; set; }

    /// <summary>
    /// Creates a PWM voltage source and adds it to the simulation.
    /// </summary>
    /// <param name="sim">The simulation to add the source to.</param>
    /// <param name="nodePos">Positive terminal node.</param>
    /// <param name="nodeNeg">Negative terminal node.</param>
    /// <param name="vHigh">Voltage during the "on" phase.</param>
    /// <param name="vLow">Voltage during the "off" phase.</param>
    /// <param name="frequency">Frequency in Hz.</param>
    /// <param name="dutyCycle">Duty cycle from 0.0 to 1.0.</param>
    public PwmVoltageSource(
        ISimulation sim,
        NodeId nodePos,
        NodeId nodeNeg,
        double vHigh,
        double vLow,
        double frequency,
        double dutyCycle) : base(sim)
    {
        VHigh = vHigh;
        VLow = vLow;
        Frequency = frequency;
        DutyCycle = dutyCycle;

        // Create with initial value at t=0
        double initialValue = GetValue(0);
        Id = sim.AddVoltageSource(nodePos, nodeNeg, initialValue);
    }

    /// <inheritdoc />
    public override double GetValue(double time)
    {
        if (Frequency <= 0)
            return VHigh; // Degenerate case: always high

        double period = 1.0 / Frequency;
        double positionInPeriod = time % period;
        double onTime = DutyCycle * period;

        return positionInPeriod < onTime ? VHigh : VLow;
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
