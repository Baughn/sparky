using System.Collections.Generic;
using Sparky.Game.Core;
using Sparky.MNA.Api;

namespace Sparky.Game.Simulation;

/// <summary>
/// Orchestrates all domain solvers and coordinates the game simulation tick.
/// </summary>
/// <remarks>
/// <para>
/// The simulation tick order follows the coupling pattern from GAME-DESIGN.md:
/// </para>
/// <list type="number">
/// <item>Rebuild topology if dirty</item>
/// <item>Electrical solve</item>
/// <item>Coupling: Electrical → Thermal (P = I²R)</item>
/// <item>Thermal solve</item>
/// <item>Coupling: Thermal → Electrical (temperature-dependent R)</item>
/// <item>Coupling: Electrical → Kinetic (motor torque)</item>
/// <item>Kinetic solve</item>
/// <item>Coupling: Kinetic → Electrical (back-EMF)</item>
/// </list>
/// <para>
/// Currently, only the electrical domain is fully implemented.
/// Thermal and kinetic are stubs with placeholder coupling methods.
/// </para>
/// </remarks>
public class GameSimulation
{
    private readonly Grid _grid;
    private readonly ISimulation _electrical;
    private readonly IThermalSolver _thermal;
    private readonly IKineticSolver _kinetic;

    /// <summary>
    /// Gets the simulation time in seconds.
    /// </summary>
    public double SimulationTime => _electrical.SimulationTime;

    /// <summary>
    /// Gets the grid managed by this simulation.
    /// </summary>
    public Grid Grid => _grid;

    /// <summary>
    /// Gets the electrical simulation (MNA solver).
    /// </summary>
    public ISimulation Electrical => _electrical;

    /// <summary>
    /// Gets the thermal solver.
    /// </summary>
    public IThermalSolver Thermal => _thermal;

    /// <summary>
    /// Gets the kinetic solver.
    /// </summary>
    public IKineticSolver Kinetic => _kinetic;

    /// <summary>
    /// Creates a GameSimulation with default solvers.
    /// </summary>
    public GameSimulation(Grid grid)
        : this(grid, new SimulationManager(), new ThermalSolverStub(), new KineticSolverStub()) { }

    /// <summary>
    /// Creates a GameSimulation with custom solvers (for testing).
    /// </summary>
    public GameSimulation(
        Grid grid,
        ISimulation electrical,
        IThermalSolver thermal,
        IKineticSolver kinetic
    )
    {
        _grid = grid;
        _electrical = electrical;
        _thermal = thermal;
        _kinetic = kinetic;

        _grid.BindSimulation(_electrical);
    }

    /// <summary>
    /// Advances the simulation by one tick.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    public void Tick(double dt)
    {
        // 1. Rebuild topology if needed
        if (_grid.IsDirty)
        {
            _grid.RebuildTopology();
        }

        // 2. Electrical solve
        _electrical.Step(dt);

        // 3. Coupling: Electrical → Thermal (P = I²R)
        ApplyElectricalToThermalCoupling();

        // 4. Thermal solve
        _thermal.Step(dt);

        // 5. Coupling: Thermal → Electrical (temperature-dependent R)
        ApplyThermalToElectricalCoupling();

        // 6. Coupling: Electrical → Kinetic (motor torque)
        ApplyElectricalToKineticCoupling();

        // 7. Kinetic solve
        _kinetic.Step(dt);

        // 8. Coupling: Kinetic → Electrical (back-EMF)
        ApplyKineticToElectricalCoupling();
    }

    /// <summary>
    /// Gets visual states for all cells after simulation.
    /// </summary>
    public Dictionary<CellId, CellVisualState> GetVisualStates()
    {
        return _grid.ComputeVisualStates();
    }

    /// <summary>
    /// Clears all state from all solvers.
    /// </summary>
    /// <remarks>
    /// Note: This clears the solvers but not the grid cells.
    /// Use Grid.RemoveCell() to remove cells.
    /// </remarks>
    public void Clear()
    {
        _electrical.Clear();
        _thermal.Clear();
        _kinetic.Clear();
    }

    /// <summary>
    /// Resets simulation time to zero without clearing state.
    /// </summary>
    public void ResetTime()
    {
        _electrical.ResetTime();
    }

    #region Coupling Methods (Placeholders)

    /// <summary>
    /// Feeds electrical power dissipation into thermal nodes.
    /// </summary>
    /// <remarks>
    /// TODO: Query resistor power from each cell and feed to its thermal node.
    /// </remarks>
    private void ApplyElectricalToThermalCoupling()
    {
        // Placeholder: In future, iterate through cells with thermal nodes
        // and set heat input based on their electrical power dissipation.
        //
        // foreach (var cell in _grid.GetAllCells())
        // {
        //     if (cell is IThermalCell thermal && cell is IElectricalCell elec)
        //     {
        //         double power = elec.GetPowerDissipation(_electrical);
        //         _thermal.SetHeatInput(thermal.ThermalNodeId, power);
        //     }
        // }
    }

    /// <summary>
    /// Updates electrical component values based on temperature.
    /// </summary>
    /// <remarks>
    /// TODO: Adjust resistor values based on thermal node temperatures.
    /// </remarks>
    private void ApplyThermalToElectricalCoupling()
    {
        // Placeholder: In future, iterate through temperature-dependent components
        // and update their values based on thermal node temperature.
        //
        // foreach (var cell in _grid.GetAllCells())
        // {
        //     if (cell is ITemperatureDependentCell tempCell)
        //     {
        //         double temp = _thermal.GetTemperature(tempCell.ThermalNodeId);
        //         tempCell.UpdateForTemperature(temp, _electrical);
        //     }
        // }
    }

    /// <summary>
    /// Computes motor torque from electrical current.
    /// </summary>
    /// <remarks>
    /// TODO: τ = k_t * I for motor cells.
    /// </remarks>
    private void ApplyElectricalToKineticCoupling()
    {
        // Placeholder: In future, iterate through motor cells
        // and set drive torque based on their current.
        //
        // foreach (var cell in _grid.GetAllCells())
        // {
        //     if (cell is IMotorCell motor)
        //     {
        //         double current = motor.GetCurrent(_electrical);
        //         double torque = motor.TorqueConstant * current;
        //         _kinetic.SetDriveTorque(motor.ShaftId, torque);
        //     }
        // }
    }

    /// <summary>
    /// Updates motor back-EMF based on angular velocity.
    /// </summary>
    /// <remarks>
    /// TODO: V_bemf = k_e * ω for motor/generator cells.
    /// </remarks>
    private void ApplyKineticToElectricalCoupling()
    {
        // Placeholder: In future, iterate through motor/generator cells
        // and update their back-EMF voltage based on shaft angular velocity.
        //
        // foreach (var cell in _grid.GetAllCells())
        // {
        //     if (cell is IMotorCell motor)
        //     {
        //         double omega = _kinetic.GetAngularVelocity(motor.ShaftId);
        //         double backEmf = motor.EmfConstant * omega;
        //         motor.UpdateBackEmf(backEmf, _electrical);
        //     }
        // }
    }

    #endregion
}
