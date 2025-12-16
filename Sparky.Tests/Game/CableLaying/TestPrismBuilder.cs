using Sparky.Game.Core;
using Sparky.Game.Core.CableLaying;

namespace Sparky.Tests.Game.CableLaying;

/// <summary>
/// Test-only prism builder that creates prisms greedily without 16³ block boundaries.
/// Used to validate cable structure independent of TopologyBuilder's block-based prism splitting.
/// </summary>
public static class TestPrismBuilder
{
    /// <summary>
    /// A test prism without block-local coordinates. Uses absolute world coordinates.
    /// </summary>
    public readonly record struct TestPrism(
        int X, int Y, int Z,
        int SizeX, int SizeY, int SizeZ,
        VoxelType Type)
    {
        public int Volume => SizeX * SizeY * SizeZ;

        /// <summary>
        /// End coordinates (exclusive).
        /// </summary>
        public (int X, int Y, int Z) End => (X + SizeX, Y + SizeY, Z + SizeZ);

        /// <summary>
        /// Checks if a position is within this prism.
        /// </summary>
        public bool Contains(VoxelPos pos) =>
            pos.X >= X && pos.X < X + SizeX &&
            pos.Y >= Y && pos.Y < Y + SizeY &&
            pos.Z >= Z && pos.Z < Z + SizeZ;
    }

    /// <summary>
    /// Builds prisms from voxel positions using greedy meshing.
    /// No size limits - prisms can span any extent.
    /// </summary>
    /// <param name="voxels">The voxel positions to build prisms from.</param>
    /// <param name="type">The voxel type (default: Conductor).</param>
    /// <returns>List of prisms covering all input voxels.</returns>
    public static List<TestPrism> BuildPrisms(IEnumerable<VoxelPos> voxels, VoxelType type = VoxelType.Conductor)
    {
        var voxelSet = new HashSet<VoxelPos>(voxels);
        var claimed = new HashSet<VoxelPos>();
        var prisms = new List<TestPrism>();

        foreach (var seed in voxelSet)
        {
            if (claimed.Contains(seed))
                continue;

            var prism = GrowPrism(seed, voxelSet, claimed);
            prisms.Add(new TestPrism(prism.X, prism.Y, prism.Z, prism.SizeX, prism.SizeY, prism.SizeZ, type));
        }

        return prisms;
    }

    /// <summary>
    /// Grows a prism greedily from a seed voxel in all directions.
    /// </summary>
    private static (int X, int Y, int Z, int SizeX, int SizeY, int SizeZ) GrowPrism(
        VoxelPos seed,
        HashSet<VoxelPos> voxelSet,
        HashSet<VoxelPos> claimed)
    {
        int minX = seed.X, maxX = seed.X;
        int minY = seed.Y, maxY = seed.Y;
        int minZ = seed.Z, maxZ = seed.Z;

        // Grow in +X and -X
        while (CanExtendRange(minX - 1, maxX, minY, maxY, minZ, maxZ, voxelSet, claimed))
            minX--;
        while (CanExtendRange(minX, maxX + 1, minY, maxY, minZ, maxZ, voxelSet, claimed))
            maxX++;

        // Grow in +Y and -Y (maintaining X extent)
        while (CanExtendRange(minX, maxX, minY - 1, maxY, minZ, maxZ, voxelSet, claimed))
            minY--;
        while (CanExtendRange(minX, maxX, minY, maxY + 1, minZ, maxZ, voxelSet, claimed))
            maxY++;

        // Grow in +Z and -Z (maintaining X and Y extent)
        while (CanExtendRange(minX, maxX, minY, maxY, minZ - 1, maxZ, voxelSet, claimed))
            minZ--;
        while (CanExtendRange(minX, maxX, minY, maxY, minZ, maxZ + 1, voxelSet, claimed))
            maxZ++;

        // Claim all voxels
        for (int z = minZ; z <= maxZ; z++)
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    claimed.Add(new VoxelPos(x, y, z));

        return (minX, minY, minZ, maxX - minX + 1, maxY - minY + 1, maxZ - minZ + 1);
    }

    /// <summary>
    /// Checks if a prism can be extended to cover the given range (inclusive).
    /// </summary>
    private static bool CanExtendRange(
        int minX, int maxX,
        int minY, int maxY,
        int minZ, int maxZ,
        HashSet<VoxelPos> voxelSet,
        HashSet<VoxelPos> claimed)
    {
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var pos = new VoxelPos(x, y, z);
                    if (!voxelSet.Contains(pos) || claimed.Contains(pos))
                        return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Calculates contact area between two test prisms (voxel faces touching).
    /// </summary>
    public static int CalculateContactArea(TestPrism a, TestPrism b)
    {
        var aEnd = a.End;
        var bEnd = b.End;

        // Adjacent in X?
        if (a.X == bEnd.X || aEnd.X == b.X)
        {
            int overlapY = RangeOverlap(a.Y, aEnd.Y, b.Y, bEnd.Y);
            int overlapZ = RangeOverlap(a.Z, aEnd.Z, b.Z, bEnd.Z);
            if (overlapY > 0 && overlapZ > 0)
                return overlapY * overlapZ;
        }

        // Adjacent in Y?
        if (a.Y == bEnd.Y || aEnd.Y == b.Y)
        {
            int overlapX = RangeOverlap(a.X, aEnd.X, b.X, bEnd.X);
            int overlapZ = RangeOverlap(a.Z, aEnd.Z, b.Z, bEnd.Z);
            if (overlapX > 0 && overlapZ > 0)
                return overlapX * overlapZ;
        }

        // Adjacent in Z?
        if (a.Z == bEnd.Z || aEnd.Z == b.Z)
        {
            int overlapX = RangeOverlap(a.X, aEnd.X, b.X, bEnd.X);
            int overlapY = RangeOverlap(a.Y, aEnd.Y, b.Y, bEnd.Y);
            if (overlapX > 0 && overlapY > 0)
                return overlapX * overlapY;
        }

        return 0;
    }

    private static int RangeOverlap(int a1, int a2, int b1, int b2)
    {
        int start = Math.Max(a1, b1);
        int end = Math.Min(a2, b2);
        return Math.Max(0, end - start);
    }

    /// <summary>
    /// Validates that all prism dimensions match the cross-section.
    /// For a W×H cross-section, each prism should have two dimensions matching W and H,
    /// with the third being the length along the cable direction.
    /// </summary>
    /// <exception cref="CableValidationException">Thrown when a prism doesn't match the cross-section.</exception>
    public static void ValidatePrismDimensions(
        IEnumerable<TestPrism> prisms,
        CrossSection crossSection)
    {
        int width = crossSection.Width;
        int height = crossSection.Height;
        var expectedDims = new[] { Math.Min(width, height), Math.Max(width, height) };

        foreach (var prism in prisms)
        {
            var dims = new[] { prism.SizeX, prism.SizeY, prism.SizeZ }.Order().ToArray();

            if (dims[0] != expectedDims[0] || dims[1] != expectedDims[1])
            {
                throw new CableValidationException(
                    $"Prism dimension violation: prism at ({prism.X}, {prism.Y}, {prism.Z}) has size " +
                    $"{prism.SizeX}×{prism.SizeY}×{prism.SizeZ}, but cross-section is " +
                    $"{width}×{height}. Expected two smallest dims to be ({expectedDims[0]}, {expectedDims[1]})");
            }
        }
    }

    /// <summary>
    /// Validates that contact areas between adjacent prisms match the cross-section.
    /// For a W×H cross-section, the contact area between connected prisms should be W×H voxels.
    /// </summary>
    /// <exception cref="CableValidationException">Thrown when contact areas don't match.</exception>
    public static void ValidatePrismContactAreas(
        IReadOnlyList<TestPrism> prisms,
        CrossSection crossSection)
    {
        int expectedArea = crossSection.Width * crossSection.Height;

        for (int i = 0; i < prisms.Count; i++)
        {
            for (int j = i + 1; j < prisms.Count; j++)
            {
                int area = CalculateContactArea(prisms[i], prisms[j]);

                if (area > 0 && area != expectedArea)
                {
                    throw new CableValidationException(
                        $"Contact area violation: prisms at ({prisms[i].X}, {prisms[i].Y}, {prisms[i].Z}) and " +
                        $"({prisms[j].X}, {prisms[j].Y}, {prisms[j].Z}) have contact area {area}, " +
                        $"but expected {expectedArea} for {crossSection.Width}×{crossSection.Height} cross-section");
                }
            }
        }
    }
}
