using System;
using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core;

/// <summary>
/// Builds MNA topology from a voxel grid and components.
/// </summary>
/// <remarks>
/// The topology builder performs these steps:
/// 1. Flood-fill to find connected conductor regions (each region = one MNA node)
/// 2. Map component terminals to their connected nodes
/// 3. Create MNA components between terminal nodes
/// </remarks>
public class TopologyBuilder
{
    /// <summary>
    /// Represents a connected region of conductor voxels.
    /// All voxels in a region share the same MNA node.
    /// </summary>
    public class ConductorRegion
    {
        public NodeId NodeId { get; set; }
        public HashSet<VoxelPos> Voxels { get; } = new();
    }

    /// <summary>
    /// Builds MNA topology from voxels and components.
    /// </summary>
    /// <param name="voxels">The voxel grid containing conductors.</param>
    /// <param name="components">The components to create MNA elements for.</param>
    /// <param name="sim">The MNA simulation to build topology in.</param>
    /// <returns>Map from voxel position to conductor region.</returns>
    public Dictionary<VoxelPos, ConductorRegion> BuildTopology(
        VoxelGrid voxels,
        IEnumerable<Component> components,
        ISimulation sim)
    {
        using var _ = sim.BeginBulkUpdate();

        // Step 1: Find all connected conductor regions via flood-fill
        var regions = FindConductorRegions(voxels);

        // Step 2: Create MNA nodes for each region
        // First, identify if any region contains a ground terminal
        var componentList = new List<Component>(components);
        var groundRegions = new HashSet<ConductorRegion>();

        foreach (var component in componentList)
        {
            if (component.Type == ComponentType.Ground)
            {
                // Ground component - mark its terminal region as ground
                foreach (var terminal in component.Terminals)
                {
                    foreach (var voxel in terminal.Voxels)
                    {
                        if (regions.TryGetValue(voxel, out var region))
                        {
                            groundRegions.Add(region);
                        }
                    }
                }
            }
        }

        // Assign nodes to regions
        var allocatedNodes = new List<NodeId>();
        foreach (var region in GetUniqueRegions(regions))
        {
            if (groundRegions.Contains(region))
            {
                region.NodeId = sim.Ground;
            }
            else
            {
                var node = sim.CreateNode();
                region.NodeId = node;
                allocatedNodes.Add(node);
            }
        }

        // Step 3: Create MNA components
        foreach (var component in componentList)
        {
            // Remove any existing MNA components
            component.RemoveMnaComponents(sim);

            // Map terminal names to node IDs
            var terminalNodes = new Dictionary<string, NodeId>();
            foreach (var terminal in component.Terminals)
            {
                // Find the region for any voxel in this terminal
                NodeId? nodeId = null;
                foreach (var voxel in terminal.Voxels)
                {
                    if (regions.TryGetValue(voxel, out var region))
                    {
                        nodeId = region.NodeId;
                        break;
                    }
                }

                // If terminal not connected to any conductor region, create isolated node
                if (!nodeId.HasValue)
                {
                    var isolatedNode = sim.CreateNode();
                    allocatedNodes.Add(isolatedNode);
                    nodeId = isolatedNode;
                }

                terminalNodes[terminal.Name] = nodeId.Value;
            }

            // Create MNA components
            component.CreateMnaComponents(sim, terminalNodes);
        }

        return regions;
    }

    /// <summary>
    /// Finds all connected conductor regions using flood-fill.
    /// </summary>
    /// <returns>Map from voxel position to its containing region.</returns>
    public Dictionary<VoxelPos, ConductorRegion> FindConductorRegions(VoxelGrid voxels)
    {
        var regions = new Dictionary<VoxelPos, ConductorRegion>();
        var visited = new HashSet<VoxelPos>();

        foreach (var pos in voxels.GetAllConductors())
        {
            if (visited.Contains(pos))
                continue;

            // Start new region with flood-fill
            var region = new ConductorRegion();
            FloodFill(voxels, pos, region, visited);

            // Map all voxels in this region
            foreach (var voxel in region.Voxels)
            {
                regions[voxel] = region;
            }
        }

        return regions;
    }

    /// <summary>
    /// Flood-fills from a starting position to find all connected conductors.
    /// </summary>
    private void FloodFill(
        VoxelGrid voxels,
        VoxelPos start,
        ConductorRegion region,
        HashSet<VoxelPos> visited)
    {
        var stack = new Stack<VoxelPos>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var pos = stack.Pop();

            if (visited.Contains(pos))
                continue;

            if (!voxels.IsConductor(pos))
                continue;

            visited.Add(pos);
            region.Voxels.Add(pos);

            // Check all 6 neighbors
            foreach (var dir in VoxelDirectionExtensions.All)
            {
                var neighbor = pos.Neighbor(dir);
                if (!visited.Contains(neighbor) && voxels.IsConductor(neighbor))
                {
                    stack.Push(neighbor);
                }
            }
        }
    }

    /// <summary>
    /// Gets the unique regions from the voxel-to-region map.
    /// </summary>
    private static IEnumerable<ConductorRegion> GetUniqueRegions(
        Dictionary<VoxelPos, ConductorRegion> regions)
    {
        var seen = new HashSet<ConductorRegion>();
        foreach (var region in regions.Values)
        {
            if (seen.Add(region))
            {
                yield return region;
            }
        }
    }
}
