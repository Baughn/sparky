using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Sparky.MNA;

namespace Sparky.Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }

    [MemoryDiagnoser]
    public class CircuitBenchmarks
    {
        private Circuit? _dcLadder;
        private Circuit? _nonLinearDc;
        private Circuit? _rcTransient;

        private const double RcDt = 1e-5;
        private const int RcSteps = 200;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _dcLadder = BuildResistorLadder(sections: 200, resistance: 1000.0, sourceVoltage: 10.0);
            _nonLinearDc = BuildDiodeClipper();
        }

        [IterationSetup(Target = nameof(SolveRcTransientSteps))]
        public void ResetTransientCircuit()
        {
            _rcTransient = BuildRcTransient();
        }

        [Benchmark(Description = "Linear DC solve: 200-resistor ladder")]
        public void SolveDcLadder()
        {
            _dcLadder!.Solve(0);
        }

        [Benchmark(Description = "Transient RC: 200 steps @ 10us")]
        public void SolveRcTransientSteps()
        {
            for (int i = 0; i < RcSteps; i++)
            {
                _rcTransient!.Solve(RcDt);
            }
        }

        [Benchmark(Description = "Non-linear DC: diode clipper")]
        public void SolveDiodeClipper()
        {
            _nonLinearDc!.Solve(0);
        }

        private static Circuit BuildResistorLadder(int sections, double resistance, double sourceVoltage)
        {
            var circuit = new Circuit();
            var ground = circuit.Ground;
            var top = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(top, ground, sourceVoltage));

            var previous = top;
            for (int i = 0; i < sections; i++)
            {
                var next = circuit.AddNode();
                circuit.AddComponent(new Resistor(previous, next, resistance));
                previous = next;
            }

            // Terminate ladder to ground to avoid an open circuit at the tail.
            circuit.AddComponent(new Resistor(previous, ground, resistance));

            return circuit;
        }

        private static Circuit BuildRcTransient()
        {
            var circuit = new Circuit();
            var ground = circuit.Ground;
            var nSource = circuit.AddNode();
            var nOut = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSource, ground, 5.0));
            circuit.AddComponent(new Resistor(nSource, nOut, 1000.0));
            circuit.AddComponent(new Capacitor(nOut, ground, 1e-6));

            return circuit;
        }

        private static Circuit BuildDiodeClipper()
        {
            var circuit = new Circuit();
            var ground = circuit.Ground;
            var nSource = circuit.AddNode();
            var nOut = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSource, ground, 10.0));
            circuit.AddComponent(new Resistor(nSource, nOut, 1000.0));
            circuit.AddComponent(new Diode(nOut, ground));

            return circuit;
        }
    }
}
