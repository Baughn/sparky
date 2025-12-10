using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core;

/// <summary>
/// Interface for cells that participate in electrical simulation.
/// </summary>
public interface IElectricalCell
{
    /// <summary>
    /// Gets the directions where this cell has electrical ports (in local coordinates, before rotation).
    /// </summary>
    /// <remarks>
    /// These are the edges of the cell that can connect to adjacent cells.
    /// A machine with a plug on one side would return only that direction.
    /// </remarks>
    IReadOnlyList<FaceDirection> GetLocalPortDirections();

    /// <summary>
    /// Creates electrical components in the simulation.
    /// Called during topology rebuild.
    /// </summary>
    /// <param name="sim">The MNA simulation.</param>
    /// <param name="ports">Map from direction (world coords, after rotation) to NodeId.</param>
    void CreateComponents(ISimulation sim, IReadOnlyDictionary<FaceDirection, NodeId> ports);

    /// <summary>
    /// Removes electrical components from the simulation.
    /// Called during topology rebuild or cell removal.
    /// </summary>
    void RemoveComponents(ISimulation sim);

    /// <summary>
    /// Updates component values without full rebuild (fast path).
    /// </summary>
    /// <returns>True if fast-path succeeded, false if rebuild needed.</returns>
    bool TryUpdateComponents(ISimulation sim);

    /// <summary>
    /// Computes visual state after simulation step.
    /// </summary>
    CellVisualState ComputeVisualState(ISimulation sim);
}

/// <summary>
/// Interface for cells that participate in thermal simulation.
/// </summary>
public interface IThermalCell
{
    /// <summary>The thermal mass in J/K.</summary>
    double ThermalMass { get; }

    /// <summary>Coupling coefficient to ambient in W/K.</summary>
    double AmbientCoupling { get; }
}

/// <summary>
/// Interface for cells that participate in kinetic simulation.
/// </summary>
public interface IKineticCell
{
    /// <summary>Moment of inertia in kg·m².</summary>
    double MomentOfInertia { get; }
}

/// <summary>
/// Base class for all grid cells.
/// </summary>
public abstract class Cell
{
    /// <summary>
    /// Unique identifier for this cell instance.
    /// Assigned by the Grid when the cell is placed.
    /// </summary>
    public CellId Id { get; internal set; }

    /// <summary>
    /// Position of this cell in 3D space (block + face + sub-position).
    /// Assigned by the Grid when the cell is placed.
    /// </summary>
    public CellPos Position { get; internal set; }

    /// <summary>
    /// Rotation within the face, in degrees (0, 90, 180, or 270).
    /// Affects how local port directions map to world directions.
    /// </summary>
    public int Rotation { get; set; }

    /// <summary>
    /// The type of this cell.
    /// </summary>
    public abstract CellType Type { get; }

    /// <summary>
    /// Transforms a local direction to world direction based on rotation.
    /// </summary>
    public FaceDirection LocalToWorld(FaceDirection local) => local.Rotate(Rotation);

    /// <summary>
    /// Transforms a world direction to local direction based on rotation.
    /// </summary>
    public FaceDirection WorldToLocal(FaceDirection world) => world.Rotate(-Rotation);

    /// <summary>
    /// Returns this cell as an IElectricalCell if it participates in electrical simulation.
    /// </summary>
    public virtual IElectricalCell? AsElectrical() => this as IElectricalCell;

    /// <summary>
    /// Returns this cell as an IThermalCell if it participates in thermal simulation.
    /// </summary>
    public virtual IThermalCell? AsThermal() => this as IThermalCell;

    /// <summary>
    /// Returns this cell as an IKineticCell if it participates in kinetic simulation.
    /// </summary>
    public virtual IKineticCell? AsKinetic() => this as IKineticCell;
}
