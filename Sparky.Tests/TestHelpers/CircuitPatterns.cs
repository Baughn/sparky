namespace Sparky.Tests.TestHelpers;

/// <summary>
/// Pre-built circuit patterns for common test scenarios.
/// Each method returns a CircuitBuilder that can be further customized.
/// </summary>
public static class CircuitPatterns
{
    /// <summary>
    /// Creates a voltage divider: Vsrc -- R1 -- mid -- R2 -- GND
    /// Node names: "src", "mid"
    /// </summary>
    /// <param name="sourceV">Source voltage</param>
    /// <param name="r1">Upper resistor (src to mid)</param>
    /// <param name="r2">Lower resistor (mid to GND)</param>
    public static CircuitBuilder VoltageDivider(double sourceV, double r1, double r2)
    {
        return new CircuitBuilder()
            .VoltageSource(sourceV, "src")
            .Resistor(r1, "src", "mid")
            .Resistor(r2, "mid", "GND");
    }

    /// <summary>
    /// Creates an RC charging circuit: Vsrc -- R -- cap -- GND
    /// Node names: "src", "cap"
    /// Time constant τ = R × C
    /// </summary>
    /// <param name="sourceV">Source voltage</param>
    /// <param name="r">Resistance in ohms</param>
    /// <param name="c">Capacitance in farads</param>
    public static CircuitBuilder RCCircuit(double sourceV, double r, double c)
    {
        return new CircuitBuilder()
            .VoltageSource(sourceV, "src")
            .Resistor(r, "src", "cap")
            .Capacitor(c, "cap");
    }

    /// <summary>
    /// Creates an RL circuit: Vsrc -- R -- ind -- GND
    /// Node names: "src", "ind"
    /// Time constant τ = L / R
    /// </summary>
    /// <param name="sourceV">Source voltage</param>
    /// <param name="r">Resistance in ohms</param>
    /// <param name="l">Inductance in henries</param>
    public static CircuitBuilder RLCircuit(double sourceV, double r, double l)
    {
        return new CircuitBuilder()
            .VoltageSource(sourceV, "src")
            .Resistor(r, "src", "ind")
            .Inductor(l, "ind");
    }

    /// <summary>
    /// Creates a series RLC circuit: Vsrc -- R -- L -- C -- GND
    /// Node names: "src", "r_out", "l_out"
    /// Resonant frequency f₀ = 1 / (2π√(LC))
    /// </summary>
    /// <param name="sourceV">Source voltage</param>
    /// <param name="r">Resistance in ohms</param>
    /// <param name="l">Inductance in henries</param>
    /// <param name="c">Capacitance in farads</param>
    public static CircuitBuilder SeriesRLC(double sourceV, double r, double l, double c)
    {
        return new CircuitBuilder()
            .VoltageSource(sourceV, "src")
            .Resistor(r, "src", "r_out")
            .Inductor(l, "r_out", "l_out")
            .Capacitor(c, "l_out");
    }

    /// <summary>
    /// Creates a simple resistive load: Vsrc -- R -- GND
    /// Node names: "src"
    /// Current I = V / R
    /// </summary>
    /// <param name="sourceV">Source voltage</param>
    /// <param name="r">Load resistance in ohms</param>
    public static CircuitBuilder ResistiveLoad(double sourceV, double r)
    {
        return new CircuitBuilder().VoltageSource(sourceV, "src").Resistor(r, "src", "GND");
    }

    /// <summary>
    /// Creates a current source with resistive load: Isrc -- R -- GND
    /// Node names: "load"
    /// Voltage V = I × R
    /// </summary>
    /// <param name="current">Source current in amps</param>
    /// <param name="r">Load resistance in ohms</param>
    public static CircuitBuilder CurrentSourceWithLoad(double current, double r)
    {
        return new CircuitBuilder()
            .CurrentSource(current, "GND", "load")
            .Resistor(r, "load", "GND");
    }
}
