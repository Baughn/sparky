using System.Collections.Generic;
using Sparky.MNA.Api;

namespace Sparky.Game.Core;

/// <summary>
/// Builds MNA topology from a voxel grid and components.
/// </summary>
/// <remarks>
/// The topology builder performs these steps:
/// 1. Find connected conductor regions by analyzing prism adjacency (not voxel flood-fill)
/// 2. Map component terminals to their connected nodes
/// 3. Create MNA components between terminal nodes
/// </remarks>
public class TopologyBuilder
{
    /// <summary>
    /// Represents a connected region of conductor prisms.
    /// All prisms in a region share the same MNA node.
    /// </summary>
    public class ConductorRegion
    {
        public NodeId NodeId { get; set; }
        public HashSet<VoxelPos> Voxels { get; } = new();
        internal List<(BlockPos Block, Prism Prism)> Prisms { get; } = new();
    }

    /// <summary>
    /// Builds MNA topology from voxels and components.
    /// </summary>
    public Dictionary<VoxelPos, ConductorRegion> BuildTopology(
        VoxelGrid voxels,
        IEnumerable<Component> components,
        ISimulation sim)
    {
        using var _ = sim.BeginBulkUpdate();

        // Step 1: Find all connected conductor regions via prism adjacency
        var regions = FindConductorRegions(voxels);

        // Step 2: Create MNA nodes for each region
        var componentList = new List<Component>(components);
        var groundRegions = new HashSet<ConductorRegion>();

        foreach (var component in componentList)
        {
            if (component.Type == ComponentType.Ground)
            {
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
            component.RemoveMnaComponents(sim);

            var terminalNodes = new Dictionary<string, NodeId>();
            foreach (var terminal in component.Terminals)
            {
                NodeId? nodeId = null;
                foreach (var voxel in terminal.Voxels)
                {
                    if (regions.TryGetValue(voxel, out var region))
                    {
                        nodeId = region.NodeId;
                        break;
                    }
                }

                if (!nodeId.HasValue)
                {
                    var isolatedNode = sim.CreateNode();
                    allocatedNodes.Add(isolatedNode);
                    nodeId = isolatedNode;
                }

                terminalNodes[terminal.Name] = nodeId.Value;
            }

            component.CreateMnaComponents(sim, terminalNodes);
        }

        return regions;
    }

    /// <summary>
    /// Finds all connected conductor regions using prism adjacency.
    /// </summary>
    public Dictionary<VoxelPos, ConductorRegion> FindConductorRegions(VoxelGrid grid)
    {
        // Collect all conductor prisms with their block positions
        var allPrisms = new List<(BlockPos Block, Prism Prism)>();
        foreach (var (block, prism) in grid.GetAllPrisms())
        {
            if (prism.Type == VoxelType.Conductor)
            {
                allPrisms.Add((block, prism));
            }
        }

        if (allPrisms.Count == 0)
            return new Dictionary<VoxelPos, ConductorRegion>();

        // Union-find to group connected prisms
        var parent = new int[allPrisms.Count];
        for (int i = 0; i < parent.Length; i++)
            parent[i] = i;

        int Find(int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(int x, int y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py)
                parent[px] = py;
        }

        // Check adjacency between all pairs of prisms
        // Optimization: group by block first, then check within-block and cross-block
        var prismsByBlock = new Dictionary<BlockPos, List<int>>();
        for (int i = 0; i < allPrisms.Count; i++)
        {
            var block = allPrisms[i].Block;
            if (!prismsByBlock.TryGetValue(block, out var list))
            {
                list = new List<int>();
                prismsByBlock[block] = list;
            }
            list.Add(i);
        }

        // Check within-block adjacency
        foreach (var (_, indices) in prismsByBlock)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    var pi = allPrisms[indices[i]].Prism;
                    var pj = allPrisms[indices[j]].Prism;
                    if (PrismsTouch(pi, pj))
                    {
                        Union(indices[i], indices[j]);
                    }
                }
            }
        }

        // Check cross-block adjacency (prisms at block boundaries)
        foreach (var (block, indices) in prismsByBlock)
        {
            // Check each of 6 neighbor blocks
            foreach (var dir in BlockFacingExtensions.All)
            {
                var neighborBlock = block.Neighbor(dir);
                if (!prismsByBlock.TryGetValue(neighborBlock, out var neighborIndices))
                    continue;

                foreach (var i in indices)
                {
                    var prismA = allPrisms[i].Prism;
                    if (!PrismAtBlockBoundary(prismA, dir))
                        continue;

                    foreach (var j in neighborIndices)
                    {
                        var prismB = allPrisms[j].Prism;
                        if (PrismsConnectAcrossBlocks(prismA, prismB, dir))
                        {
                            Union(i, j);
                        }
                    }
                }
            }
        }

        // Build regions from union-find result
        var regionsByRoot = new Dictionary<int, ConductorRegion>();
        for (int i = 0; i < allPrisms.Count; i++)
        {
            var root = Find(i);
            if (!regionsByRoot.TryGetValue(root, out var region))
            {
                region = new ConductorRegion();
                regionsByRoot[root] = region;
            }
            region.Prisms.Add(allPrisms[i]);
        }

        // Build voxel-to-region map by expanding prisms
        var result = new Dictionary<VoxelPos, ConductorRegion>();
        foreach (var region in regionsByRoot.Values)
        {
            foreach (var (block, prism) in region.Prisms)
            {
                var end = prism.End;
                for (int z = prism.LocalZ; z < end.Z; z++)
                {
                    for (int y = prism.LocalY; y < end.Y; y++)
                    {
                        for (int x = prism.LocalX; x < end.X; x++)
                        {
                            var voxelPos = VoxelPos.FromBlockLocal(block, x, y, z);
                            result[voxelPos] = region;
                            region.Voxels.Add(voxelPos);
                        }
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if two prisms in the same block touch (share a face).
    /// </summary>
    private static bool PrismsTouch(Prism a, Prism b)
    {
        var aEnd = a.End;
        var bEnd = b.End;

        // Check if they overlap in 2 dimensions and are adjacent in the third

        // Adjacent in X?
        if ((a.LocalX == bEnd.X || aEnd.X == b.LocalX) &&
            RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y) &&
            RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z))
            return true;

        // Adjacent in Y?
        if ((a.LocalY == bEnd.Y || aEnd.Y == b.LocalY) &&
            RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
            RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z))
            return true;

        // Adjacent in Z?
        if ((a.LocalZ == bEnd.Z || aEnd.Z == b.LocalZ) &&
            RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
            RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if prism is at the block boundary in the given direction.
    /// </summary>
    private static bool PrismAtBlockBoundary(Prism p, BlockFacing facing)
    {
        return facing switch
        {
            BlockFacing.West => p.LocalX == 0,           // -X
            BlockFacing.East => p.LocalX + p.SizeX == 16, // +X
            BlockFacing.Down => p.LocalY == 0,           // -Y
            BlockFacing.Up => p.LocalY + p.SizeY == 16,   // +Y
            BlockFacing.North => p.LocalZ == 0,          // -Z
            BlockFacing.South => p.LocalZ + p.SizeZ == 16, // +Z
            _ => false
        };
    }

    /// <summary>
    /// Checks if two prisms connect across a block boundary.
    /// prismA is in the block, prismB is in the neighbor block in direction facing.
    /// </summary>
    private static bool PrismsConnectAcrossBlocks(Prism a, Prism b, BlockFacing facing)
    {
        var aEnd = a.End;
        var bEnd = b.End;

        return facing switch
        {
            // A is at +X boundary, B is at -X boundary (local X = 0)
            BlockFacing.East => aEnd.X == 16 && b.LocalX == 0 &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.West => a.LocalX == 0 && bEnd.X == 16 &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.Up => aEnd.Y == 16 && b.LocalY == 0 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.Down => a.LocalY == 0 && bEnd.Y == 16 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalZ, aEnd.Z, b.LocalZ, bEnd.Z),

            BlockFacing.South => aEnd.Z == 16 && b.LocalZ == 0 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y),

            BlockFacing.North => a.LocalZ == 0 && bEnd.Z == 16 &&
                RangesOverlap(a.LocalX, aEnd.X, b.LocalX, bEnd.X) &&
                RangesOverlap(a.LocalY, aEnd.Y, b.LocalY, bEnd.Y),

            _ => false
        };
    }

    /// <summary>
    /// Checks if two ranges [a1, a2) and [b1, b2) overlap.
    /// </summary>
    private static bool RangesOverlap(int a1, int a2, int b1, int b2)
    {
        return a1 < b2 && b1 < a2;
    }

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
