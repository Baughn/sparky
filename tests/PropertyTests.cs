using NUnit.Framework;
using CsCheck;
using Sparky.MNA.Core;
using Sparky.Tests.TestHelpers;

namespace Sparky.Tests;

/// <summary>
/// Property-based tests for circuit simulation using CsCheck.
/// </summary>
[TestFixture]
public class PropertyTests
{
    // Use relative tolerance for property tests since values span wide ranges
    // The solver achieves ~1e-7 relative error, so 1e-6 gives headroom
    private const double RelTol = 1e-6;

    #region Helpers

    private static (double vMid, double vSrc) SolveVoltageDivider(double voltage, double r1, double r2)
    {
        var circuit = new Circuit();
        var nSrc = circuit.AddNode();
        var nMid = circuit.AddNode();
        var ground = circuit.Nodes[0];

        circuit.AddComponent(new VoltageSource(nSrc, ground, voltage));
        circuit.AddComponent(new Resistor(nSrc, nMid, r1));
        circuit.AddComponent(new Resistor(nMid, ground, r2));

        circuit.Solve(0);
        return (nMid.Voltage, nSrc.Voltage);
    }

    private static (double current, double vNode) SolveSimpleResistor(double voltage, double resistance)
    {
        var circuit = new Circuit();
        var nSrc = circuit.AddNode();
        var ground = circuit.Nodes[0];

        circuit.AddComponent(new VoltageSource(nSrc, ground, voltage));
        circuit.AddComponent(new Resistor(nSrc, ground, resistance));

        circuit.Solve(0);
        return (nSrc.Voltage / resistance, nSrc.Voltage);
    }

    private static (double iIn, double iOut) SolveKCLCircuit(double voltage, double r1, double r2, double r3)
    {
        var circuit = new Circuit();
        var nSrc = circuit.AddNode();
        var n1 = circuit.AddNode();
        var ground = circuit.Nodes[0];

        circuit.AddComponent(new VoltageSource(nSrc, ground, voltage));
        circuit.AddComponent(new Resistor(nSrc, n1, r1));
        circuit.AddComponent(new Resistor(n1, ground, r2));
        circuit.AddComponent(new Resistor(n1, ground, r3));

        circuit.Solve(0);

        var vN1 = n1.Voltage;
        var vSrc = nSrc.Voltage;
        return ((vSrc - vN1) / r1, vN1 / r2 + vN1 / r3);
    }

    private static (double vR1, double vR2, double vR3, double vSrc) SolveKVLCircuit(double voltage, double r1, double r2, double r3)
    {
        var circuit = new Circuit();
        var nSrc = circuit.AddNode();
        var n1 = circuit.AddNode();
        var n2 = circuit.AddNode();
        var ground = circuit.Nodes[0];

        circuit.AddComponent(new VoltageSource(nSrc, ground, voltage));
        circuit.AddComponent(new Resistor(nSrc, n1, r1));
        circuit.AddComponent(new Resistor(n1, n2, r2));
        circuit.AddComponent(new Resistor(n2, ground, r3));

        circuit.Solve(0);

        return (nSrc.Voltage - n1.Voltage, n1.Voltage - n2.Voltage, n2.Voltage, nSrc.Voltage);
    }

    private static double SolveParallelResistors(double voltage, double rSeries, double r1, double r2)
    {
        var circuit = new Circuit();
        var nSrc = circuit.AddNode();
        var n1 = circuit.AddNode();
        var ground = circuit.Nodes[0];

        circuit.AddComponent(new VoltageSource(nSrc, ground, voltage));
        circuit.AddComponent(new Resistor(nSrc, n1, rSeries));
        circuit.AddComponent(new Resistor(n1, ground, r1));
        circuit.AddComponent(new Resistor(n1, ground, r2));

        circuit.Solve(0);
        return n1.Voltage;
    }

    private static double SolveCurrentSourceOhm(double current, double resistance)
    {
        var circuit = new Circuit();
        var n1 = circuit.AddNode();
        var ground = circuit.Nodes[0];

        circuit.AddComponent(new CurrentSource(ground, n1, current));
        circuit.AddComponent(new Resistor(n1, ground, resistance));

        circuit.Solve(0);
        return n1.Voltage;
    }

    private static double SolveCurrentSuperposition(int numSources, double currentPerSource, double resistance)
    {
        var circuit = new Circuit();
        var n1 = circuit.AddNode();
        var ground = circuit.Nodes[0];

        for (int i = 0; i < numSources; i++)
            circuit.AddComponent(new CurrentSource(ground, n1, currentPerSource));
        circuit.AddComponent(new Resistor(n1, ground, resistance));

        circuit.Solve(0);
        return n1.Voltage;
    }

    /// <summary>
    /// Build an X×Y resistor grid with voltage sources on left and right edges.
    /// Returns (power generated, power dissipated).
    /// </summary>
    private static (double pGen, double pDissipated) SolveResistorGrid(
        int width, int height, double vLeft, double vRight, double resistance)
    {
        var circuit = new Circuit();
        var ground = circuit.Nodes[0];

        // Create node grid: nodes[x,y]
        var nodes = new Node[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                nodes[x, y] = circuit.AddNode();

        // Voltage source on left edge (x=0) at bottom node, referenced to ground
        var leftSource = new VoltageSource(nodes[0, 0], ground, vLeft);
        circuit.AddComponent(leftSource);

        // Voltage source on right edge (x=width-1) at bottom node, referenced to ground
        var rightSource = new VoltageSource(nodes[width - 1, 0], ground, vRight);
        circuit.AddComponent(rightSource);

        // Horizontal resistors
        for (int x = 0; x < width - 1; x++)
            for (int y = 0; y < height; y++)
                circuit.AddComponent(new Resistor(nodes[x, y], nodes[x + 1, y], resistance));

        // Vertical resistors (only if height > 1)
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height - 1; y++)
                circuit.AddComponent(new Resistor(nodes[x, y], nodes[x, y + 1], resistance));

        circuit.Solve(0);

        // Power from sources (negative current = delivering power)
        double pGen = -leftSource.Current * vLeft - rightSource.Current * vRight;

        // Power dissipated in all resistors
        double pDissipated = 0;
        foreach (var comp in circuit.Components)
        {
            if (comp is Resistor r)
            {
                double v1 = r.Node1.Id == 0 ? 0 : r.Node1.Voltage;
                double v2 = r.Node2.Id == 0 ? 0 : r.Node2.Voltage;
                double vDrop = v1 - v2;
                pDissipated += vDrop * vDrop / r.Resistance;
            }
        }

        return (pGen, pDissipated);
    }

    #endregion

    /// <summary>
    /// Property: V_mid = V_source * R2 / (R1 + R2)
    /// </summary>
    [Test]
    public void VoltageDivider_RatioIsCorrect()
    {
        Gen.Select(
            Gen.Double[1, 1000],
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000]
        ).Sample((voltage, r1, r2) =>
        {
            var (vMid, _) = SolveVoltageDivider(voltage, r1, r2);
            var expected = voltage * r2 / (r1 + r2);
            var relErr = Math.Abs(vMid - expected) / Math.Abs(expected);
            if (relErr >= RelTol)
                throw new Exception($"vMid={vMid}, expected={expected}, relErr={relErr:e}");
        });
    }

    /// <summary>
    /// Property: Power generated = Power dissipated
    /// </summary>
    [Test]
    public void PowerConservation_SourceEqualsLoad()
    {
        Gen.Select(
            Gen.Double[1, 1000],
            Gen.Double[1, 1_000_000]
        ).Sample((voltage, resistance) =>
        {
            var (current, _) = SolveSimpleResistor(voltage, resistance);
            var pGen = voltage * current;
            var pDissipated = current * current * resistance;
            var relErr = Math.Abs(pGen - pDissipated) / Math.Max(Math.Abs(pGen), 1e-15);
            if (relErr >= RelTol)
                throw new Exception($"pGen={pGen}, pDissipated={pDissipated}, relErr={relErr:e}");
        });
    }

    /// <summary>
    /// Property: Current in = Current out (KCL)
    /// </summary>
    [Test]
    public void KCL_CurrentsBalanceAtNode()
    {
        Gen.Select(
            Gen.Double[1, 1000],
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000]
        ).Sample((voltage, r1, r2, r3) =>
        {
            var (iIn, iOut) = SolveKCLCircuit(voltage, r1, r2, r3);
            var relErr = Math.Abs(iIn - iOut) / Math.Max(Math.Abs(iIn), 1e-15);
            if (relErr >= RelTol)
                throw new Exception($"iIn={iIn}, iOut={iOut}, relErr={relErr:e}");
        });
    }

    /// <summary>
    /// Property: V_source = V_R1 + V_R2 + V_R3 (KVL around loop)
    /// </summary>
    [Test]
    public void KVL_VoltageDropsSumToSource()
    {
        Gen.Select(
            Gen.Double[1, 1000],
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000]
        ).Sample((voltage, r1, r2, r3) =>
        {
            var (vR1, vR2, vR3, vSrc) = SolveKVLCircuit(voltage, r1, r2, r3);
            var sumDrops = vR1 + vR2 + vR3;
            var relErr = Math.Abs(sumDrops - vSrc) / Math.Max(Math.Abs(vSrc), 1e-15);
            if (relErr >= RelTol)
                throw new Exception($"V={voltage}, r1={r1}, r2={r2}, r3={r3}, sumDrops={sumDrops}, vSrc={vSrc}, relErr={relErr:e}");
        });
    }

    /// <summary>
    /// Property: V_node = V * R_eq / (R_series + R_eq) where R_eq = R1*R2/(R1+R2)
    /// </summary>
    [Test]
    public void ParallelResistors_EquivalentResistance()
    {
        Gen.Select(
            Gen.Double[1, 1000],
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000]
        ).Sample((voltage, rSeries, r1, r2) =>
        {
            var vNode = SolveParallelResistors(voltage, rSeries, r1, r2);
            var rEq = r1 * r2 / (r1 + r2);
            var expected = voltage * rEq / (rSeries + rEq);
            var relErr = Math.Abs(vNode - expected) / Math.Max(Math.Abs(expected), 1e-15);
            if (relErr >= RelTol)
                throw new Exception($"V={voltage}, rS={rSeries}, r1={r1}, r2={r2}, vNode={vNode}, expected={expected}, relErr={relErr:e}");
        });
    }

    /// <summary>
    /// Property: V = I * R (Ohm's law for current source)
    /// </summary>
    [Test]
    public void OhmsLaw_CurrentSourceDrivesVoltage()
    {
        Gen.Select(
            Gen.Double[0.001, 100],
            Gen.Double[1, 1_000_000]
        ).Sample((current, resistance) =>
        {
            var voltage = SolveCurrentSourceOhm(current, resistance);
            var expected = current * resistance;
            var relErr = Math.Abs(voltage - expected) / Math.Max(Math.Abs(expected), 1e-15);
            if (relErr >= RelTol)
                throw new Exception($"I={current}, R={resistance}, V={voltage}, expected={expected}, relErr={relErr:e}");
        });
    }

    /// <summary>
    /// Property: n parallel current sources produce n*I*R voltage
    /// </summary>
    [Test]
    public void CurrentSuperposition_SourcesAddLinearly()
    {
        Gen.Select(
            Gen.Int[1, 10],
            Gen.Double[0.001, 10],
            Gen.Double[1, 1_000_000]
        ).Sample((numSources, currentPerSource, resistance) =>
        {
            var voltage = SolveCurrentSuperposition(numSources, currentPerSource, resistance);
            var expected = numSources * currentPerSource * resistance;
            var relErr = Math.Abs(voltage - expected) / Math.Max(Math.Abs(expected), 1e-15);
            if (relErr >= RelTol)
                throw new Exception($"n={numSources}, I={currentPerSource}, R={resistance}, V={voltage}, expected={expected}, relErr={relErr:e}");
        });
    }

    /// <summary>
    /// Property: Power conservation in resistor grid (P_generated = P_dissipated).
    /// Grid has many non-ground node pairs, sensitive to off-diagonal stamp errors.
    /// </summary>
    [Test]
    public void ResistorGrid_PowerConservation()
    {
        // Looser tolerance for grid - more nodes means more accumulated floating-point error,
        // especially when vLeft ≈ vRight (small power, relative error amplified)
        const double gridTol = 1e-3;

        Gen.Select(
            Gen.Int[2, 6],            // width
            Gen.Int[2, 6],            // height
            Gen.Double[1, 100],       // vLeft
            Gen.Double[-100, 100],    // vRight (can be negative or same as left)
            Gen.Double[1, 10_000]     // resistance
        ).Sample((width, height, vLeft, vRight, resistance) =>
        {
            var (pGen, pDissipated) = SolveResistorGrid(width, height, vLeft, vRight, resistance);
            var absErr = Math.Abs(pGen - pDissipated);
            var relErr = absErr / Math.Max(Math.Abs(pGen), 1e-15);
            // Use whichever is smaller - relative for large power, absolute for small power
            var err = Math.Min(relErr, absErr);
            if (err >= gridTol)
                throw new Exception($"grid={width}x{height}, vL={vLeft}, vR={vRight}, R={resistance}, pGen={pGen}, pDiss={pDissipated}, relErr={relErr:e}, absErr={absErr:e}");
        });
    }

    /// <summary>
    /// Deliberate failure to observe shrinking behavior.
    /// </summary>
    [Test]
    [Category("ShrinkComparison")]
    [Explicit("Deliberately failing test for shrinking comparison")]
    public void ShrinkTest_FailsWhenResistorsClose()
    {
        Gen.Select(
            Gen.Double[1, 1_000_000],
            Gen.Double[1, 1_000_000]
        ).Sample((r1, r2) =>
        {
            var ratio = r1 / r2;
            return ratio < 0.99 || ratio > 1.01;
        }, iter: 10000);  // More iterations to hit failure
    }
}
