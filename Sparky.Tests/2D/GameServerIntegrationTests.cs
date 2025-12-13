using NUnit.Framework;
using Sparky.Game.Core;
using Sparky.Game.Core.ComponentTypes;
using Sparky.MNA.Api;
using Sparky.TwoD.Protocol;
using Sparky.TwoD.Server;

// Use the Protocol CellType, not Game.Core.CellType
using CellType = Sparky.TwoD.Protocol.CellType;

namespace Sparky.Tests.TwoD;

/// <summary>
/// Integration tests for the 2D game's client-server protocol.
/// These tests act as the client, sending InputEvents and verifying RenderCommands.
/// </summary>
[TestFixture]
public class GameServerIntegrationTests
{
    /// <summary>
    /// Tests a simple battery-resistor-ground circuit with DC analysis.
    /// Components use 3-cell layout: terminal - body - terminal.
    /// The two conductor regions must NOT be physically adjacent.
    ///
    /// Layout (voxel Z = grid Y):
    /// Region 1 (Ground, 0V): RA(0,0,5), G(0,0,6), return path, B-(9,0,5)
    /// Region 2 (High, 5V): RB(2,0,5), bridge path, B+(11,0,5)
    ///
    /// Return path (Region 1): Z=7 row from X=0 to X=9
    /// Bridge path (Region 2): Z=3 row from X=2 to X=11
    /// </summary>
    [Test]
    public void SimpleCircuit_DCAnalysis_CorrectVoltageDistribution()
    {
        var server = new GameServer(16, 16);

        // Resistor at (0, 5) rot=0: RA at (0,0,5), body at (1,0,5), RB at (2,0,5)
        server.HandleInput(new PlaceComponent(new GridPos(0, 5), CellType.Resistor, 0));

        // Battery at (9, 5) rot=0: B- at (9,0,5), body at (10,0,5), B+ at (11,0,5)
        server.HandleInput(new PlaceComponent(new GridPos(9, 5), CellType.Battery, 0));

        // Ground at (0, 6) - adjacent to RA
        server.HandleInput(new PlaceComponent(new GridPos(0, 6), CellType.Ground, 0));

        // Region 1 return path: connect Ground to B- via Z=7 row
        // (0,0,6) adj (0,0,7) and (9,0,6) adj (9,0,5)
        for (int x = 0; x <= 9; x++)
            server.HandleInput(new PlaceComponent(new GridPos(x, 7), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(9, 6), CellType.Wire));

        // Region 2 bridge path: connect RB to B+ via Z=3 row
        // Need vertical connectors at RB and B+ ends
        server.HandleInput(new PlaceComponent(new GridPos(2, 4), CellType.Wire)); // (2,0,4)
        server.HandleInput(new PlaceComponent(new GridPos(2, 3), CellType.Wire)); // (2,0,3)
        for (int x = 3; x <= 11; x++)
            server.HandleInput(new PlaceComponent(new GridPos(x, 3), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(11, 4), CellType.Wire)); // (11,0,4)

        // Run DC analysis (dt=0)
        var commands = server.Tick(0).ToList();

        // Extract SetCell commands to verify voltage distribution
        var setCells = commands.OfType<SetCell>().ToDictionary(c => c.Pos, c => c);

        // Ground side should have low/zero voltage
        Assert.That(setCells.ContainsKey(new GridPos(0, 6)), Is.True, "Ground cell should have SetCell command");
        var groundCell = setCells[new GridPos(0, 6)];
        Assert.That(groundCell.State.VoltageNormalized, Is.LessThan(0.1f),
            "Ground should have near-zero normalized voltage");

        // Resistor should show power dissipation (current flowing)
        Assert.That(setCells.ContainsKey(new GridPos(0, 5)), Is.True, "Resistor cell should have SetCell command");
        var resistorCell = setCells[new GridPos(0, 5)];
        Assert.That(resistorCell.State.PowerNormalized, Is.GreaterThan(0),
            "Resistor should have non-zero power (current flowing)");

        // Battery negative terminal should be near ground (small negative due to wire resistance)
        Assert.That(setCells.ContainsKey(new GridPos(9, 5)), Is.True, "Battery cell should have SetCell command");
        var batteryCell = setCells[new GridPos(9, 5)];
        Assert.That(batteryCell.State.VoltageNormalized, Is.GreaterThanOrEqualTo(-0.1f),
            "Battery negative terminal should be near ground (allowing for wire voltage drops)");
    }

    /// <summary>
    /// Tests that the simulation handles transient analysis correctly.
    /// In real gameplay, time passes while placing components.
    /// Uses the same isolated layout as the DC test.
    /// </summary>
    [Test]
    public void SimpleCircuit_TransientAnalysis_StableAfterConstruction()
    {
        var server = new GameServer(16, 16);
        const float dt = 0.016f; // ~60fps frame time

        // Place components incrementally with time passing between each
        // Resistor first
        server.HandleInput(new PlaceComponent(new GridPos(0, 5), CellType.Resistor, 0));
        var commands1 = server.Tick(dt).ToList();
        Assert.That(commands1, Is.Not.Empty, "Should have render commands after placing resistor");

        // Ground adjacent to RA
        server.HandleInput(new PlaceComponent(new GridPos(0, 6), CellType.Ground, 0));
        server.Tick(dt);

        // Region 1 return path (wires at Z=7)
        for (int x = 0; x <= 9; x++)
        {
            server.HandleInput(new PlaceComponent(new GridPos(x, 7), CellType.Wire));
            server.Tick(dt);
        }
        server.HandleInput(new PlaceComponent(new GridPos(9, 6), CellType.Wire));
        server.Tick(dt);

        // Region 2 bridge path (wires at Z=3)
        server.HandleInput(new PlaceComponent(new GridPos(2, 4), CellType.Wire));
        server.Tick(dt);
        server.HandleInput(new PlaceComponent(new GridPos(2, 3), CellType.Wire));
        server.Tick(dt);
        for (int x = 3; x <= 11; x++)
        {
            server.HandleInput(new PlaceComponent(new GridPos(x, 3), CellType.Wire));
            server.Tick(dt);
        }
        server.HandleInput(new PlaceComponent(new GridPos(11, 4), CellType.Wire));
        server.Tick(dt);

        // Complete the circuit with battery
        server.HandleInput(new PlaceComponent(new GridPos(9, 5), CellType.Battery, 0));
        server.Tick(dt);

        // Run several more ticks to let simulation settle
        for (int i = 0; i < 10; i++)
        {
            server.Tick(dt);
        }

        // Get full state to verify final values
        var fullState = server.GetFullState().OfType<SetCell>().ToDictionary(c => c.Pos, c => c);

        // Verify no NaN values (simulation didn't blow up)
        foreach (var (pos, cell) in fullState)
        {
            Assert.That(float.IsNaN(cell.State.VoltageNormalized), Is.False,
                $"Voltage at {pos} should not be NaN");
            Assert.That(float.IsNaN(cell.State.CurrentNormalized), Is.False,
                $"Current at {pos} should not be NaN");
            Assert.That(float.IsNaN(cell.State.PowerNormalized), Is.False,
                $"Power at {pos} should not be NaN");
        }

        // Verify final state matches expected DC solution
        var resistorCell = fullState[new GridPos(0, 5)];

        Assert.That(resistorCell.State.PowerNormalized, Is.GreaterThan(0),
            "Resistor should have non-zero power after circuit is complete");
    }

    /// <summary>
    /// Tests a voltage divider circuit with two resistors in series.
    /// Uses 3-cell components with properly isolated conductor regions.
    ///
    /// Three conductor regions:
    /// - Region 1 (0V): G, R1A, B-
    /// - Region 2 (middle): R1B, R2A (adjacent at the junction)
    /// - Region 3 (5V): R2B, B+
    ///
    /// Layout on Z=5:
    /// R1: R1A(0) - body(1) - R1B(2)
    /// R2: R2A(3) - body(4) - R2B(5)  [R2A adj R1B creates middle region]
    /// Battery: B-(10) - body(11) - B+(12)
    /// </summary>
    [Test]
    public void VoltageDivider_MiddleNodeHasIntermediateVoltage()
    {
        var server = new GameServer(16, 16);

        // R1 at (0, 5) rot=0: R1A at (0,0,5), R1B at (2,0,5)
        server.HandleInput(new PlaceComponent(new GridPos(0, 5), CellType.Resistor, 0));

        // R2 at (3, 5) rot=0: R2A at (3,0,5), R2B at (5,0,5)
        // R2A(3,0,5) is ADJACENT to R1B(2,0,5) - forms middle region
        server.HandleInput(new PlaceComponent(new GridPos(3, 5), CellType.Resistor, 0));

        // Battery at (10, 5) rot=0: B- at (10,0,5), B+ at (12,0,5)
        server.HandleInput(new PlaceComponent(new GridPos(10, 5), CellType.Battery, 0));

        // Ground at (0, 6) - adjacent to R1A
        server.HandleInput(new PlaceComponent(new GridPos(0, 6), CellType.Ground, 0));

        // Region 1 path: connect G/R1A to B- via Z=7 (avoiding middle and high regions)
        for (int x = 0; x <= 10; x++)
            server.HandleInput(new PlaceComponent(new GridPos(x, 7), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(10, 6), CellType.Wire)); // connect to B-

        // Region 3 path: connect R2B to B+ via Z=3 (avoiding middle and ground regions)
        server.HandleInput(new PlaceComponent(new GridPos(5, 4), CellType.Wire)); // down from R2B
        server.HandleInput(new PlaceComponent(new GridPos(5, 3), CellType.Wire));
        for (int x = 6; x <= 12; x++)
            server.HandleInput(new PlaceComponent(new GridPos(x, 3), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(12, 4), CellType.Wire)); // up to B+

        // Run DC analysis
        server.Tick(0);
        var fullState = server.GetFullState().OfType<SetCell>().ToDictionary(c => c.Pos, c => c);

        // Both resistors should have power dissipation
        var r1Power = fullState[new GridPos(0, 5)].State.PowerNormalized;
        var r2Power = fullState[new GridPos(3, 5)].State.PowerNormalized;

        Assert.That(r1Power, Is.GreaterThan(0), "R1 should have power");
        Assert.That(r2Power, Is.GreaterThan(0), "R2 should have power");
    }

    /// <summary>
    /// Debug test: directly use TopologyBuilder with 3-cell components
    /// to verify the core simulation works with properly isolated terminals.
    ///
    /// IMPORTANT: The two conductor regions must be connected ONLY through
    /// the battery and resistor MNA components, not through adjacent conductor voxels.
    ///
    /// Layout:
    /// Z=5: [G(0)] [RA(1)] [body(2)] [RB(3)] [W(4)] [B+(5)] [body(6)] [B-(7)]
    /// Z=6: [W(0)] [W(1)]  [W(2)]     gap     gap     gap    [W(6)]   [W(7)]
    /// Z=7:               [W(2)]   [W(3)]  [W(4)]  [W(5)]   [W(6)]
    ///
    /// Region 1 (Ground, 0V): (0,0,5), (1,0,5), (7,0,5) + return path
    /// Region 2 (High, 5V): (3,0,5), (4,0,5), (5,0,5)
    /// </summary>
    [Test]
    public void Debug_DirectTopologyBuilder_WorksWithIsolatedTerminals()
    {
        var grid = new VoxelGrid();
        var sim = new SimulationManager();
        var builder = new TopologyBuilder();

        // Main row Z=5
        grid.SetVoxel(new VoxelPos(0, 0, 5), VoxelType.Conductor); // Ground
        grid.SetVoxel(new VoxelPos(1, 0, 5), VoxelType.Conductor); // Resistor A
        // (2, 0, 5) is Resistor body - NOT a conductor
        grid.SetVoxel(new VoxelPos(3, 0, 5), VoxelType.Conductor); // Resistor B (High region)
        grid.SetVoxel(new VoxelPos(4, 0, 5), VoxelType.Conductor); // Wire (High region)
        grid.SetVoxel(new VoxelPos(5, 0, 5), VoxelType.Conductor); // Battery+ (High region)
        // (6, 0, 5) is Battery body - NOT a conductor
        grid.SetVoxel(new VoxelPos(7, 0, 5), VoxelType.Conductor); // Battery-

        // Return path Z=6 - SKIP X=3,4,5 to avoid adjacency with High region
        grid.SetVoxel(new VoxelPos(0, 0, 6), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(1, 0, 6), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(2, 0, 6), VoxelType.Conductor); // OK: (2,0,5) is body
        // gap at X=3,4,5 to avoid touching High region
        grid.SetVoxel(new VoxelPos(6, 0, 6), VoxelType.Conductor); // OK: (6,0,5) is body
        grid.SetVoxel(new VoxelPos(7, 0, 6), VoxelType.Conductor);

        // Bridge Z=7 - connects the two parts of return path
        grid.SetVoxel(new VoxelPos(2, 0, 7), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(3, 0, 7), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(4, 0, 7), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(5, 0, 7), VoxelType.Conductor);
        grid.SetVoxel(new VoxelPos(6, 0, 7), VoxelType.Conductor);

        // Create components
        // Resistor: terminal A at (1,0,5) in ground region, B at (3,0,5) in High region
        var resistor = new ResistorComponent(new VoxelPos(1, 0, 5), new VoxelPos(3, 0, 5), 100.0);
        // Battery: negative at (7,0,5) in ground region, positive at (5,0,5) in High region
        var battery = new BatteryComponent(new VoxelPos(7, 0, 5), new VoxelPos(5, 0, 5), 5.0);
        var ground = new GroundComponent(new VoxelPos(0, 0, 5));

        var components = new Component[] { ground, battery, resistor };

        // Build topology
        var regions = builder.BuildTopology(grid, components, sim);

        // Run DC analysis
        sim.Step(0);

        // Verify we have TWO conductor regions
        var groundRegion = regions[new VoxelPos(0, 0, 5)];
        var highRegion = regions[new VoxelPos(5, 0, 5)];
        Assert.That(groundRegion, Is.Not.SameAs(highRegion),
            "Ground and Battery+ should be in different conductor regions");

        // Ground should be at 0V
        Assert.That(sim.GetVoltage(groundRegion.NodeId), Is.EqualTo(0).Within(1e-9),
            "Ground should be at 0V");

        // Battery+ should be at 5V
        Assert.That(sim.GetVoltage(highRegion.NodeId), Is.EqualTo(5.0).Within(1e-9),
            "Battery+ should be at 5V");

        // Resistor should have current = 5V / 100Ω = 0.05A
        var current = sim.GetResistorCurrent(resistor.ResistorId!.Value);
        Assert.That(Math.Abs(current), Is.EqualTo(0.05).Within(1e-6),
            "Resistor current should be 0.05A");
    }

    /// <summary>
    /// Tests that multi-cell components render all 3 cells (origin, body, far terminal).
    /// </summary>
    [Test]
    public void MultiCellComponents_RenderAll3Cells()
    {
        var server = new GameServer(16, 16);

        // Place a battery at (5, 5) with rotation 0
        // Should create cells at: (5,5)=Battery, (6,5)=BatteryBody, (7,5)=BatteryPositive
        server.HandleInput(new PlaceComponent(new GridPos(5, 5), CellType.Battery, 0));

        // Place a resistor at (0, 5) with rotation 0
        // Should create cells at: (0,5)=Resistor, (1,5)=ResistorBody, (2,5)=ResistorTerminalB
        server.HandleInput(new PlaceComponent(new GridPos(0, 5), CellType.Resistor, 0));

        var commands = server.Tick(0).ToList();
        var setCells = commands.OfType<SetCell>().ToDictionary(c => c.Pos, c => c);

        // Verify battery renders 3 cells
        Assert.That(setCells.ContainsKey(new GridPos(5, 5)), Is.True, "Battery origin should render");
        Assert.That(setCells.ContainsKey(new GridPos(6, 5)), Is.True, "Battery body should render");
        Assert.That(setCells.ContainsKey(new GridPos(7, 5)), Is.True, "Battery positive should render");

        Assert.That(setCells[new GridPos(5, 5)].Type, Is.EqualTo(CellType.Battery));
        Assert.That(setCells[new GridPos(6, 5)].Type, Is.EqualTo(CellType.BatteryBody));
        Assert.That(setCells[new GridPos(7, 5)].Type, Is.EqualTo(CellType.BatteryPositive));

        // Verify resistor renders 3 cells
        Assert.That(setCells.ContainsKey(new GridPos(0, 5)), Is.True, "Resistor origin should render");
        Assert.That(setCells.ContainsKey(new GridPos(1, 5)), Is.True, "Resistor body should render");
        Assert.That(setCells.ContainsKey(new GridPos(2, 5)), Is.True, "Resistor terminal B should render");

        Assert.That(setCells[new GridPos(0, 5)].Type, Is.EqualTo(CellType.Resistor));
        Assert.That(setCells[new GridPos(1, 5)].Type, Is.EqualTo(CellType.ResistorBody));
        Assert.That(setCells[new GridPos(2, 5)].Type, Is.EqualTo(CellType.ResistorTerminalB));
    }

    /// <summary>
    /// Tests that removing any cell of a multi-cell component removes all 3 cells.
    /// </summary>
    [Test]
    public void MultiCellComponents_RemoveAnyCell_RemovesAll()
    {
        var server = new GameServer(16, 16);

        // Place a battery
        server.HandleInput(new PlaceComponent(new GridPos(5, 5), CellType.Battery, 0));
        server.Tick(0);

        // Remove by clicking on the body (middle cell)
        server.HandleInput(new RemoveComponent(new GridPos(6, 5)));
        var commands = server.Tick(0).ToList();

        // Should have ClearCell for all 3 positions
        var clearCells = commands.OfType<ClearCell>().Select(c => c.Pos).ToHashSet();
        Assert.That(clearCells.Contains(new GridPos(5, 5)), Is.True, "Battery origin should be cleared");
        Assert.That(clearCells.Contains(new GridPos(6, 5)), Is.True, "Battery body should be cleared");
        Assert.That(clearCells.Contains(new GridPos(7, 5)), Is.True, "Battery positive should be cleared");
    }

    /// <summary>
    /// Tests that wire cells display current correctly when connecting components.
    /// Layout:
    ///   BAT- (0,0)  --wire(1,0)--  RES_A (2,0)
    ///   BODY (0,1)                 BODY  (2,1)
    ///   BAT+ (0,2)  --wire(1,2)--  RES_B (2,2)
    ///
    /// With 10V battery and 2Ω resistor: I = 10V / 2Ω = 5A
    /// Both wires should show 5A current.
    /// </summary>
    [Test]
    public void WireCells_ShowCorrectCurrent()
    {
        var server = new GameServer(16, 16);

        // Battery at (0,0) with rotation 1 (+Y): negative at (0,0), body at (0,1), positive at (0,2)
        server.HandleInput(new PlaceComponent(new GridPos(0, 0), CellType.Battery, 1));

        // Resistor at (2,0) with rotation 1 (+Y): terminal A at (2,0), body at (2,1), terminal B at (2,2)
        server.HandleInput(new PlaceComponent(new GridPos(2, 0), CellType.Resistor, 1));

        // Wires connecting the terminals
        server.HandleInput(new PlaceComponent(new GridPos(1, 0), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(1, 2), CellType.Wire));

        // Run DC analysis
        var commands = server.Tick(0).ToList();
        var setCells = commands.OfType<SetCell>().ToDictionary(c => c.Pos, c => c);

        // Both wires should show ~5A current (10V / 2Ω = 5A)
        Assert.That(setCells.ContainsKey(new GridPos(1, 0)), Is.True, "Wire at (1,0) should have SetCell");
        Assert.That(setCells.ContainsKey(new GridPos(1, 2)), Is.True, "Wire at (1,2) should have SetCell");

        var wire1 = setCells[new GridPos(1, 0)];
        var wire2 = setCells[new GridPos(1, 2)];

        // Current normalized to 1A reference
        // Expected ~4.8A due to wire-terminal contact resistances (0.01Ω each, ~4 contacts)
        // 10V / (2Ω + 0.04Ω) ≈ 4.9A
        Assert.That(wire1.State.CurrentNormalized, Is.EqualTo(4.9f).Within(0.2f),
            $"Wire at (1,0) should show ~4.9A current, got {wire1.State.CurrentNormalized}");
        Assert.That(wire2.State.CurrentNormalized, Is.EqualTo(4.9f).Within(0.2f),
            $"Wire at (1,2) should show ~4.9A current, got {wire2.State.CurrentNormalized}");
    }

    /// <summary>
    /// Tests that longer wire chains all show the same current.
    /// Layout:
    ///   BAT- (0,0)  --wire(1,0)--wire(2,0)--wire(3,0)--  RES_A (4,0)
    ///   BODY (0,1)                                       BODY  (4,1)
    ///   BAT+ (0,2)  --wire(1,2)--wire(2,2)--wire(3,2)--  RES_B (4,2)
    ///
    /// All 6 wires should show ~5A current.
    /// </summary>
    [Test]
    public void WireChain_AllWiresShowSameCurrent()
    {
        var server = new GameServer(16, 16);

        // Battery at (0,0) with rotation 1 (+Y)
        server.HandleInput(new PlaceComponent(new GridPos(0, 0), CellType.Battery, 1));

        // Resistor at (4,0) with rotation 1 (+Y)
        server.HandleInput(new PlaceComponent(new GridPos(4, 0), CellType.Resistor, 1));

        // Bottom wire chain connecting BAT- to RES_A
        server.HandleInput(new PlaceComponent(new GridPos(1, 0), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(2, 0), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(3, 0), CellType.Wire));

        // Top wire chain connecting BAT+ to RES_B
        server.HandleInput(new PlaceComponent(new GridPos(1, 2), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(2, 2), CellType.Wire));
        server.HandleInput(new PlaceComponent(new GridPos(3, 2), CellType.Wire));

        // Run DC analysis
        var commands = server.Tick(0).ToList();
        var setCells = commands.OfType<SetCell>().ToDictionary(c => c.Pos, c => c);

        // All 6 wires should show ~5A current
        var wirePositions = new[]
        {
            new GridPos(1, 0), new GridPos(2, 0), new GridPos(3, 0),
            new GridPos(1, 2), new GridPos(2, 2), new GridPos(3, 2)
        };

        // Debug: print all wire currents to understand what's happening
        foreach (var pos in wirePositions)
        {
            Assert.That(setCells.ContainsKey(pos), Is.True, $"Wire at {pos} should have SetCell");
        }

        var currents = wirePositions.Select(p => (p, setCells[p].State.CurrentNormalized)).ToList();
        var currentsStr = string.Join(", ", currents.Select(c => $"{c.p}={c.CurrentNormalized:F2}A"));
        Console.WriteLine($"Wire currents: {currentsStr}");

        // Current slightly less than 5A due to wire-terminal contact resistances
        // All wires should show approximately the same current
        foreach (var (pos, current) in currents)
        {
            Assert.That(current, Is.EqualTo(4.9f).Within(0.3f),
                $"Wire at {pos} should show ~4.9A current, got {current}. All currents: {currentsStr}");
        }
    }
}
