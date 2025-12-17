using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;
using Sparky.MNA.Api;
using Sparky.MNA.Core;
using Sparky.Tests.Game.CableLaying;

namespace Sparky.Benchmarks {
    public class Program {
        public static void Main(string[] args) {
            var config = ManualConfig
                .Create(DefaultConfig.Instance)
                .WithOption(ConfigOptions.JoinSummary, true)
                .AddJob(Job.InProcess);

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }

    [MemoryDiagnoser]
    public class CircuitBenchmarks {
        private Circuit? _dcLadder;
        private Circuit? _nonLinearDc;
        private Circuit? _rcTransient;
        private Circuit? _dcLadderDynamic;

        private const double RcDt = 1e-5;
        private const int RcSteps = 200;

        [GlobalSetup]
        public void GlobalSetup() {
            _dcLadder = BuildResistorLadder(sections: 200, resistance: 1000.0, sourceVoltage: 10.0);
            _nonLinearDc = BuildDiodeClipper();
            _dcLadderDynamic = BuildResistorLadder(
                sections: 200,
                resistance: 1000.0,
                sourceVoltage: 0.0
            );
        }

        [Benchmark(Description = "Linear DC solve: 200-resistor ladder")]
        public void SolveDcLadder() {
            _dcLadder!.Solve(0);
        }

        [Benchmark(Description = "Transient RC: 200 steps")]
        public void SolveRcTransientSteps() {
            _rcTransient = BuildRcTransient();
            for (int i = 0; i < RcSteps; i++) {
                _rcTransient!.Solve(RcDt);
            }
        }

        [Benchmark(Description = "Linear DC sweep: 200-resistor ladder, varying source")]
        public void SolveDcLadderDynamicRhs() {
            var source = (VoltageSource)_dcLadderDynamic!.Components[0];
            for (int i = 0; i < 100; i++) {
                source.Voltage = i % 2 == 0 ? 10.0 : 5.0;
                _dcLadderDynamic.Solve(0);
            }
        }

        [Benchmark(Description = "Non-linear DC: diode clipper")]
        public void SolveDiodeClipper() {
            _nonLinearDc!.Solve(0);
        }

        [Benchmark(Description = "Microgrid transient: gen + PV + battery + load steps")]
        public void SolveMicrogridTransient() {
            var circuit = new Circuit();
            var ground = circuit.Ground;
            var bus = circuit.AddNode();

            // Generator with internal resistance and adjustable terminal voltage
            var genNode = circuit.AddNode();
            var generator = new VoltageSource(genNode, ground, 0.0);
            var genInternal = new Resistor(genNode, bus, 0.2);
            circuit.AddComponent(generator);
            circuit.AddComponent(genInternal);

            // Battery as a large capacitor with small ESR
            var batteryNode = circuit.AddNode();
            circuit.AddComponent(new Resistor(bus, batteryNode, 0.05));
            circuit.AddComponent(new Capacitor(batteryNode, ground, 50.0));

            // PV array modeled as a time-varying current source injecting into the bus
            var pv = new CurrentSource(ground, bus, 0.0);
            circuit.AddComponent(pv);

            // Base load and an oven that toggles on/off
            var baseLoad = new Resistor(bus, ground, 60.0);
            var oven = new Resistor(bus, ground, 1e9); // starts disconnected
            circuit.AddComponent(baseLoad);
            circuit.AddComponent(oven);

            double dt = 0.001; // 1 ms
            double time = 0.0;
            int steps = 2000; // 2 seconds simulated

            for (int i = 0; i < steps; i++) {
                time += dt;

                // Generator ramps up, then sags slightly later in the run
                if (time < 0.5) {
                    generator.Voltage = Lerp(0.0, 230.0, time / 0.5);
                } else if (time < 1.5) {
                    generator.Voltage = 230.0;
                } else {
                    generator.Voltage = 190.0;
                }

                // PV follows a daylight sine lobe peaking mid-run
                double daylight = Math.Clamp(Math.Sin(Math.PI * (time / 2.0)), 0.0, 1.0);
                pv.Current = 30.0 * daylight; // inject up to 30 A into the bus

                // Oven kicks on for a heavy load window
                bool ovenOn = time >= 0.8 && time <= 1.2;
                oven.Resistance = ovenOn ? 6.0 : 1e9;

                // Base load softens as non-critical loads shed
                baseLoad.Resistance = time >= 1.6 ? 30.0 : 60.0;

                circuit.Solve(dt);
            }
        }

        [Benchmark(Description = "Transformer + bridge rectifier + cap filter")]
        public void SolveTransformerRectifier() {
            var circuit = new Circuit();
            var ground = circuit.Ground;

            var nPri = circuit.AddNode();
            var nSecHot = circuit.AddNode();
            var nSecReturn = circuit.AddNode();
            var nBus = circuit.AddNode();

            var source = new VoltageSource(nPri, ground, 0.0);
            circuit.AddComponent(source);

            // Step-down transformer (primary to secondary)
            var transformer = new Transformer(nPri, ground, nSecHot, nSecReturn, 0.25);
            circuit.AddComponent(transformer);

            // Full-wave bridge to DC bus
            circuit.AddComponent(new Diode(nSecHot, nBus)); // sec hot -> bus +
            circuit.AddComponent(new Diode(nSecReturn, nBus)); // sec return -> bus +
            circuit.AddComponent(new Diode(ground, nSecHot)); // bus - (ground) -> sec hot
            circuit.AddComponent(new Diode(ground, nSecReturn)); // bus - (ground) -> sec return

            circuit.AddComponent(new Capacitor(nBus, ground, 0.0047)); // 4700 uF smoothing cap
            circuit.AddComponent(new Resistor(nBus, ground, 20.0)); // load

            double dt = 0.0001; // 100 us
            double time = 0.0;
            int steps = 2000; // 0.2 s (~10 cycles at 50 Hz)
            double amplitude = 325.0; // ~230 Vrms peak
            double freq = 50.0;

            for (int i = 0; i < steps; i++) {
                time += dt;
                source.Voltage = amplitude * Math.Sin(2 * Math.PI * freq * time);
                circuit.Solve(dt);
            }
        }

        [Benchmark(Description = "Boost-style converter with PWM switch")]
        public void SolvePwmBoostConverter() {
            var circuit = new Circuit();
            var ground = circuit.Ground;

            var nSource = circuit.AddNode();
            var nSwitch = circuit.AddNode();
            var nOut = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSource, ground, 12.0));
            circuit.AddComponent(new Inductor(nSource, nSwitch, 200e-6));
            var pwmSwitch = new Resistor(nSwitch, ground, 1e9); // OFF initially
            circuit.AddComponent(pwmSwitch);

            // Diode feeds the output from the switch node
            circuit.AddComponent(new Diode(nSwitch, nOut));
            circuit.AddComponent(new Capacitor(nOut, ground, 470e-6));
            circuit.AddComponent(new Resistor(nOut, ground, 4.0)); // load

            double freq = 20_000.0; // 20 kHz PWM
            double period = 1.0 / freq; // 50 us
            double dt = period / 50.0; // 1 us (50 steps per cycle)
            double runtime = 0.01; // 10 ms total (~200 cycles)
            int steps = (int)(runtime / dt);

            double time = 0.0;
            double onR = 0.02;
            double offR = 1e9;

            for (int i = 0; i < steps; i++) {
                time += dt;
                double duty = 0.25 + 0.45 * Math.Min(time / runtime, 1.0); // ramp duty 25% -> 70%
                double phase = time % period;
                bool switchOn = phase < duty * period;
                pwmSwitch.Resistance = switchOn ? onR : offR;

                circuit.Solve(dt);
            }
        }

        private static Circuit BuildResistorLadder(
            int sections,
            double resistance,
            double sourceVoltage
        ) {
            var circuit = new Circuit();
            var ground = circuit.Ground;
            var top = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(top, ground, sourceVoltage));

            var pL = top;
            var pR = top;
            for (int i = 0; i < sections; i++) {
                var nL = circuit.AddNode();
                var nR = circuit.AddNode();
                circuit.AddComponent(new Resistor(pL, nL, resistance));
                circuit.AddComponent(new Resistor(nL, nR, resistance));
                circuit.AddComponent(new Resistor(pR, nR, resistance));
                pL = nL;
                pR = nR;
            }

            // Terminate ladder to ground to avoid an open circuit at the tail.
            circuit.AddComponent(new Resistor(pL, ground, resistance));
            circuit.AddComponent(new Resistor(pR, ground, resistance));

            return circuit;
        }

        private static Circuit BuildRcTransient() {
            var circuit = new Circuit();
            var ground = circuit.Ground;
            var nSource = circuit.AddNode();
            var nOut = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSource, ground, 5.0));
            circuit.AddComponent(new Resistor(nSource, nOut, 1000.0));
            circuit.AddComponent(new Capacitor(nOut, ground, 1e-6));

            return circuit;
        }

        private static Circuit BuildDiodeClipper() {
            var circuit = new Circuit();
            var ground = circuit.Ground;
            var nSource = circuit.AddNode();
            var nOut = circuit.AddNode();

            circuit.AddComponent(new VoltageSource(nSource, ground, 10.0));
            circuit.AddComponent(new Resistor(nSource, nOut, 1000.0));
            circuit.AddComponent(new Diode(nOut, ground));

            return circuit;
        }

        private static double Lerp(double from, double to, double t) => from + (to - from) * t;
    }

    [MemoryDiagnoser]
    public class TopologyBuilderBenchmarks {
        private VoxelGrid _gridFloodFill = null!;
        private VoxelGrid _gridRandomized = null!;
        private TopologyBuilder _builder = null!;

        [GlobalSetup]
        public void GlobalSetup() {
            _builder = new TopologyBuilder();
            _gridFloodFill = BuildLargeWire(BuildMethod.FloodFill);
            _gridRandomized = BuildLargeWire(BuildMethod.Randomized);
        }

        [Benchmark(Description = "Large wire: build grid (flood-fill order)")]
        public VoxelGrid BuildGridFloodFill() {
            return BuildLargeWire(BuildMethod.FloodFill);
        }

        [Benchmark(Description = "Large wire: build grid (randomized order)")]
        public VoxelGrid BuildGridRandomized() {
            return BuildLargeWire(BuildMethod.Randomized);
        }

        [Benchmark(Description = "Large wire: find regions (flood-fill grid)")]
        public int FindRegionsFloodFill() {
            var sim = new SimulationManager();
            var regions = _builder.BuildTopology(_gridFloodFill, [], sim);
            return regions.Count;
        }

        [Benchmark(Description = "Large wire: find regions (randomized grid)")]
        public int FindRegionsRandomized() {
            var sim = new SimulationManager();
            var regions = _builder.BuildTopology(_gridRandomized, [], sim);
            return regions.Count;
        }

        [Benchmark(Description = "Large wire: full solve with line optimization")]
        public double FullSolveWithOptimization() {
            var sim = new SimulationManager();
            sim.EnableLineOptimization = true;
            _builder.BuildTopology(_gridFloodFill, [], sim);
            sim.Step(0);
            return sim.GetStats().OptimizedNodeCount;
        }

        private enum BuildMethod { FloodFill, Randomized }

        private static VoxelGrid BuildLargeWire(BuildMethod method) {
            var grid = new VoxelGrid();
            var positions = GetLargeWirePositions().ToList();

            if (method == BuildMethod.Randomized) {
                var rng = new Random(42);
                positions = positions.OrderBy(_ => rng.Next()).ToList();
            }

            foreach (var pos in positions) {
                var isTerminal = pos.Z == 0 || pos.Z == 191;
                grid.SetVoxel(pos, isTerminal ? VoxelType.Conductor : VoxelType.ResistiveConductor);
            }

            return grid;
        }

        private static IEnumerable<VoxelPos> GetLargeWirePositions() {
            // Terminal A: single voxel at center of Z=0 face
            yield return new VoxelPos(1, 1, 0);

            // Wire: 3x3x190 from z=1 to z=190
            for (int z = 1; z <= 190; z++)
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        yield return new VoxelPos(x, y, z);

            // Terminal B: 3x3 at z=191
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    yield return new VoxelPos(x, y, 191);
        }
    }

    [MemoryDiagnoser]
    public class SnapPositionBenchmarks {
        private SnapPositionTests _testFixture = null!;

        [GlobalSetup]
        public void GlobalSetup() {
            _testFixture = new SnapPositionTests();
            _testFixture.SetUp();
        }

        [Benchmark(Description = "Snap position: 1x1 cross-section")]
        public void FindSnapPosition_1x1() {
            _testFixture.SnapPosition_TouchesSurface_WithCorrectContactCount(
                CrossSection.Size1x1, VoxelDirection.XPos);
        }

        [Benchmark(Description = "Snap position: 3x5 cross-section")]
        public void FindSnapPosition_3x5() {
            _testFixture.SnapPosition_TouchesSurface_WithCorrectContactCount(
                CrossSection.Size3x5, VoxelDirection.XPos);
        }
    }
}
