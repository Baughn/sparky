using Sparky.MNA.Api;

namespace Sparky.Tests.TestHelpers;

/// <summary>
/// Fluent builder for creating test circuits with named nodes.
/// Reduces boilerplate from 8-10 lines to 2-3 lines for common patterns.
/// </summary>
public class CircuitBuilder
{
    private readonly SimulationManager _sim = new();
    private readonly Dictionary<string, NodeId> _nodes = new();

    /// <summary>
    /// Access to the underlying SimulationManager for advanced operations.
    /// </summary>
    public SimulationManager Sim => _sim;

    /// <summary>
    /// The ground node.
    /// </summary>
    public NodeId Ground => _sim.Ground;

    /// <summary>
    /// Gets or creates a node by name. Nodes are created lazily on first reference.
    /// Use "GND" (case-insensitive) for ground.
    /// </summary>
    public NodeId Node(string name)
    {
        if (name.Equals("GND", StringComparison.OrdinalIgnoreCase))
            return _sim.Ground;

        if (!_nodes.TryGetValue(name, out var node))
        {
            node = _sim.CreateNode();
            _nodes[name] = node;
        }
        return node;
    }

    /// <summary>
    /// Adds a voltage source. Use "GND" for ground connection.
    /// </summary>
    public CircuitBuilder VoltageSource(double voltage, string from, string to = "GND")
    {
        _sim.AddVoltageSource(Node(from), Node(to), voltage);
        return this;
    }

    /// <summary>
    /// Adds a current source (current flows from 'from' to 'to').
    /// </summary>
    public CircuitBuilder CurrentSource(double current, string from, string to = "GND")
    {
        _sim.AddCurrentSource(Node(from), Node(to), current);
        return this;
    }

    /// <summary>
    /// Adds a resistor between two nodes. Use "GND" for ground connection.
    /// </summary>
    public CircuitBuilder Resistor(double resistance, string from, string to)
    {
        _sim.AddResistor(Node(from), Node(to), resistance);
        return this;
    }

    /// <summary>
    /// Adds a capacitor. Defaults to ground connection.
    /// </summary>
    public CircuitBuilder Capacitor(double capacitance, string from, string to = "GND")
    {
        _sim.AddCapacitor(Node(from), Node(to), capacitance);
        return this;
    }

    /// <summary>
    /// Adds an inductor. Defaults to ground connection.
    /// </summary>
    public CircuitBuilder Inductor(double inductance, string from, string to = "GND")
    {
        _sim.AddInductor(Node(from), Node(to), inductance);
        return this;
    }

    /// <summary>
    /// Adds a diode (anode to cathode). Use "GND" for ground connection.
    /// </summary>
    public CircuitBuilder Diode(string anode, string cathode = "GND")
    {
        _sim.AddDiode(Node(anode), Node(cathode));
        return this;
    }

    /// <summary>
    /// Adds a switch between two nodes. Returns the SwitchId for state control.
    /// </summary>
    public SwitchId Switch(string from, string to, bool closed = false)
    {
        return _sim.AddSwitch(Node(from), Node(to), closed);
    }

    /// <summary>
    /// Runs a single simulation step.
    /// </summary>
    /// <param name="dt">Time step in seconds (default 0.001 = 1ms)</param>
    public CircuitBuilder Step(double dt = 0.001)
    {
        _sim.Step(dt);
        return this;
    }

    /// <summary>
    /// Runs multiple simulation steps.
    /// </summary>
    /// <param name="count">Number of steps</param>
    /// <param name="dt">Time step in seconds (default 0.001 = 1ms)</param>
    public CircuitBuilder StepN(int count, double dt = 0.001)
    {
        for (int i = 0; i < count; i++)
            _sim.Step(dt);
        return this;
    }

    /// <summary>
    /// Gets the voltage at a named node.
    /// </summary>
    public double V(string nodeName) => _sim.GetVoltage(Node(nodeName));
}
