# Menger Sponge Preview Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace translucent voxel preview with opaque Menger sponge pattern to eliminate internal geometry visibility.

**Architecture:** Each preview voxel becomes 20 sub-voxels (1/3 scale) in a Menger sponge iteration-1 pattern. Two-tone shading (exterior 100%, interior 60%) provides depth cues. Fully opaque rendering eliminates depth sorting issues.

**Tech Stack:** C#, Vintage Story API (MeshData, BlockFacing, CubeMeshUtil)

---

### Task 1: Add Menger Pattern Constants

**Files:**
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewMesh.cs:14-23`

**Step 1: Add the Menger pattern constants after `ZFightingScale`**

Add these constants and static data after line 23:

```csharp
    /// <summary>
    /// Size of one sub-voxel (1/3 of a voxel) for Menger sponge rendering.
    /// </summary>
    private const float SubVoxelSize = VoxelSize / 3f;

    /// <summary>
    /// Shading for exterior faces (outer shell of Menger sponge).
    /// </summary>
    private const float ExteriorShading = 1.0f;

    /// <summary>
    /// Shading for interior faces (facing into the hollow).
    /// </summary>
    private const float InteriorShading = 0.6f;

    /// <summary>
    /// The 7 axial positions removed in a Menger sponge iteration 1.
    /// Center + 6 face-centers.
    /// </summary>
    private static readonly HashSet<(int, int, int)> RemovedPositions = new() {
        (1, 1, 1),  // center
        (0, 1, 1), (2, 1, 1),  // X-axis face centers
        (1, 0, 1), (1, 2, 1),  // Y-axis face centers
        (1, 1, 0), (1, 1, 2),  // Z-axis face centers
    };
```

**Step 2: Build and verify no compile errors**

Run: `dotnet build src/mod/`
Expected: Build succeeded

**Step 3: Commit**

```bash
jj describe -m "Add Menger sponge pattern constants"
jj new
```

---

### Task 2: Update GetMaterialColor to Opaque

**Files:**
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewMesh.cs:163-181`

**Step 1: Change default alpha from 128 to 255**

Change line 169 from:
```csharp
    public static int GetMaterialColor(Material material, byte alpha = 128) {
```

To:
```csharp
    public static int GetMaterialColor(Material material, byte alpha = 255) {
```

**Step 2: Build and verify no compile errors**

Run: `dotnet build src/mod/`
Expected: Build succeeded

**Step 3: Commit**

```bash
jj describe -m "Change preview material color to opaque (alpha 255)"
jj new
```

---

### Task 3: Create Sub-Voxel Face Addition Method

**Files:**
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewMesh.cs`

**Step 1: Add helper method to check if a face is interior**

Add after `ApplyShading` method (around line 161):

```csharp
    /// <summary>
    /// Determines if a sub-voxel face is interior (facing a removed position).
    /// </summary>
    private static bool IsInteriorFace(int sx, int sy, int sz, int dx, int dy, int dz) {
        return RemovedPositions.Contains((sx + dx, sy + dy, sz + dz));
    }
```

**Step 2: Add method to add a sub-voxel to the mesh**

Add after `IsInteriorFace`:

```csharp
    /// <summary>
    /// Adds a single sub-voxel's faces to the mesh with Menger sponge shading.
    /// </summary>
    private static void AddSubVoxelToMesh(
        MeshData mesh,
        float voxelRelX, float voxelRelY, float voxelRelZ,
        int sx, int sy, int sz,
        int rgba,
        HashSet<(int, int, int)> subVoxelSet) {

        // Sub-voxel position relative to parent voxel corner
        float subX = voxelRelX + sx * SubVoxelSize;
        float subY = voxelRelY + sy * SubVoxelSize;
        float subZ = voxelRelZ + sz * SubVoxelSize;

        var faces = new (BlockFacing Face, int Dx, int Dy, int Dz)[] {
            (BlockFacing.NORTH, 0, 0, -1),
            (BlockFacing.EAST, 1, 0, 0),
            (BlockFacing.SOUTH, 0, 0, 1),
            (BlockFacing.WEST, -1, 0, 0),
            (BlockFacing.UP, 0, 1, 0),
            (BlockFacing.DOWN, 0, -1, 0),
        };

        foreach (var (face, dx, dy, dz) in faces) {
            // Skip face if adjacent sub-voxel exists in the set
            if (subVoxelSet.Contains((sx + dx, sy + dy, sz + dz)))
                continue;

            // Determine shading: interior faces are darker
            float shading = IsInteriorFace(sx, sy, sz, dx, dy, dz)
                ? InteriorShading
                : ExteriorShading;

            AddSubVoxelFaceToMesh(mesh, face, subX, subY, subZ, rgba, shading);
        }
    }
```

**Step 3: Add method to add a sub-voxel face to the mesh**

Add after `AddSubVoxelToMesh`:

```csharp
    /// <summary>
    /// Adds a single sub-voxel face quad to the mesh.
    /// </summary>
    private static void AddSubVoxelFaceToMesh(
        MeshData mesh,
        BlockFacing face,
        float x, float y, float z,
        int argbColor,
        float shading) {
        int baseVertex = mesh.VerticesCount;

        int faceIndex = face.Index;
        int vertexOffset = faceIndex * 4 * 3;
        int uvOffset = faceIndex * 4 * 2;

        int shadedColor = ApplyShading(argbColor, shading);

        float halfSubVoxel = SubVoxelSize * 0.5f;

        for (int i = 0; i < 4; i++) {
            float vx = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 0];
            float vy = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 1];
            float vz = CubeMeshUtil.CubeVertices[vertexOffset + i * 3 + 2];

            // Scale from -1..1 to sub-voxel size, apply z-fighting scale
            float wx = x + halfSubVoxel + (vx * halfSubVoxel * ZFightingScale);
            float wy = y + halfSubVoxel + (vy * halfSubVoxel * ZFightingScale);
            float wz = z + halfSubVoxel + (vz * halfSubVoxel * ZFightingScale);

            float u = CubeMeshUtil.CubeUvCoords[uvOffset + i * 2 + 0];
            float v = CubeMeshUtil.CubeUvCoords[uvOffset + i * 2 + 1];

            mesh.AddVertex(wx, wy, wz, u, v, shadedColor);
        }

        mesh.AddIndex(baseVertex + 0);
        mesh.AddIndex(baseVertex + 1);
        mesh.AddIndex(baseVertex + 2);
        mesh.AddIndex(baseVertex + 0);
        mesh.AddIndex(baseVertex + 2);
        mesh.AddIndex(baseVertex + 3);
    }
```

**Step 4: Build and verify no compile errors**

Run: `dotnet build src/mod/`
Expected: Build succeeded

**Step 5: Commit**

```bash
jj describe -m "Add sub-voxel mesh generation methods for Menger pattern"
jj new
```

---

### Task 4: Replace AddVoxelToMesh with Menger Pattern

**Files:**
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewMesh.cs:69-98`

**Step 1: Replace AddVoxelToMesh implementation**

Replace the entire `AddVoxelToMesh` method with:

```csharp
    /// <summary>
    /// Adds a voxel as a Menger sponge (iteration 1) to the mesh.
    /// Generates 20 sub-voxels, culling faces between adjacent sub-voxels
    /// and adjacent preview voxels.
    /// </summary>
    private static void AddVoxelToMesh(
        MeshData mesh,
        PreviewVoxel voxel,
        HashSet<(int X, int Y, int Z)> voxelSet,
        int originX, int originY, int originZ) {

        // Position relative to mesh origin
        float relX = (voxel.X - originX) * VoxelSize;
        float relY = (voxel.Y - originY) * VoxelSize;
        float relZ = (voxel.Z - originZ) * VoxelSize;

        // Build the set of sub-voxels that exist (excluding removed positions)
        // Also exclude sub-voxels on edges where adjacent preview voxels exist
        var subVoxelSet = new HashSet<(int, int, int)>();

        for (int sx = 0; sx < 3; sx++)
        for (int sy = 0; sy < 3; sy++)
        for (int sz = 0; sz < 3; sz++) {
            if (RemovedPositions.Contains((sx, sy, sz)))
                continue;
            subVoxelSet.Add((sx, sy, sz));
        }

        // For each direction, if an adjacent voxel exists, add "virtual" sub-voxels
        // at positions 3/-1 to enable face culling at voxel boundaries
        if (voxelSet.Contains((voxel.X - 1, voxel.Y, voxel.Z))) {
            for (int sy = 0; sy < 3; sy++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((2, sy, sz)))
                    subVoxelSet.Add((-1, sy, sz));
        }
        if (voxelSet.Contains((voxel.X + 1, voxel.Y, voxel.Z))) {
            for (int sy = 0; sy < 3; sy++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((0, sy, sz)))
                    subVoxelSet.Add((3, sy, sz));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y - 1, voxel.Z))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((sx, 2, sz)))
                    subVoxelSet.Add((sx, -1, sz));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y + 1, voxel.Z))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sz = 0; sz < 3; sz++)
                if (!RemovedPositions.Contains((sx, 0, sz)))
                    subVoxelSet.Add((sx, 3, sz));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y, voxel.Z - 1))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sy = 0; sy < 3; sy++)
                if (!RemovedPositions.Contains((sx, sy, 2)))
                    subVoxelSet.Add((sx, sy, -1));
        }
        if (voxelSet.Contains((voxel.X, voxel.Y, voxel.Z + 1))) {
            for (int sx = 0; sx < 3; sx++)
            for (int sy = 0; sy < 3; sy++)
                if (!RemovedPositions.Contains((sx, sy, 0)))
                    subVoxelSet.Add((sx, sy, 3));
        }

        // Generate sub-voxels
        for (int sx = 0; sx < 3; sx++)
        for (int sy = 0; sy < 3; sy++)
        for (int sz = 0; sz < 3; sz++) {
            if (RemovedPositions.Contains((sx, sy, sz)))
                continue;
            AddSubVoxelToMesh(mesh, relX, relY, relZ, sx, sy, sz, voxel.Rgba, subVoxelSet);
        }
    }
```

**Step 2: Build and verify no compile errors**

Run: `dotnet build src/mod/`
Expected: Build succeeded

**Step 3: Commit**

```bash
jj describe -m "Replace voxel mesh with Menger sponge pattern (20 sub-voxels)"
jj new
```

---

### Task 5: Update Mesh Allocation Size

**Files:**
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewMesh.cs:47-48`

**Step 1: Increase mesh allocation for sub-voxels**

Each voxel now generates up to 20 sub-voxels instead of 1. Update line 48:

From:
```csharp
        var mesh = new MeshData(24 * voxels.Count, 36 * voxels.Count, withUv: true, withRgba: true, withFlags: true);
```

To:
```csharp
        // 20 sub-voxels per voxel, 24 vertices and 36 indices per sub-voxel max
        var mesh = new MeshData(24 * 20 * voxels.Count, 36 * 20 * voxels.Count, withUv: true, withRgba: true, withFlags: true);
```

**Step 2: Build and verify no compile errors**

Run: `dotnet build src/mod/`
Expected: Build succeeded

**Step 3: Commit**

```bash
jj describe -m "Increase mesh allocation for Menger sub-voxels"
jj new
```

---

### Task 6: Remove Old Face Addition Method

**Files:**
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewMesh.cs`

**Step 1: Remove the old AddFaceToMesh method**

Delete the entire `AddFaceToMesh` method (the one that takes `float shading` as the last parameter with per-face directional shading). This is now replaced by `AddSubVoxelFaceToMesh`.

The method to delete is approximately lines 100-148 (after Task 4 changes).

**Step 2: Build and verify no compile errors**

Run: `dotnet build src/mod/`
Expected: Build succeeded

**Step 3: Commit**

```bash
jj describe -m "Remove unused AddFaceToMesh method"
jj new
```

---

### Task 7: Update Renderer to Opaque Mode

**Files:**
- Modify: `src/mod/vsintegration/Preview/VoxelPreviewRenderer.cs:13-14,113-115`

**Step 1: Update class docstring**

Change line 13-14 from:
```csharp
/// <summary>
/// Renders preview voxels for all players as transparent ghost blocks.
/// </summary>
```

To:
```csharp
/// <summary>
/// Renders preview voxels for all players as opaque Menger sponge patterns.
/// </summary>
```

**Step 2: Disable alpha blending**

Change line 114 from:
```csharp
        rapi.GlToggleBlend(true, EnumBlendMode.Standard);
```

To:
```csharp
        rapi.GlToggleBlend(false);
```

**Step 3: Build and verify no compile errors**

Run: `dotnet build src/mod/`
Expected: Build succeeded

**Step 4: Commit**

```bash
jj describe -m "Switch preview renderer to opaque mode"
jj new
```

---

### Task 8: Visual Testing

**Step 1: Build release mod**

Run: `dotnet build -c Release src/mod/`
Expected: Build succeeded, creates `src/mod/bin/Sparky.zip`

**Step 2: Test in-game**

1. Launch Vintage Story with mod installed
2. Equip wire tool
3. Start placing a cable preview
4. Verify:
   - Menger sponge pattern visible (hollow center)
   - Exterior faces brighter than interior faces
   - Material colors correct (copper orange, lead gray, etc.)
   - No internal geometry artifacts
   - No depth sorting issues from any angle

**Step 3: Final commit if changes needed**

If any adjustments needed, make them and commit.

---

### Task 9: Update Design Document Status

**Files:**
- Modify: `docs/plans/2025-12-29-menger-preview-design.md:3`

**Step 1: Mark design as implemented**

Change line 3 from:
```markdown
**Status**: Design complete
```

To:
```markdown
**Status**: Implemented
```

**Step 2: Commit**

```bash
jj describe -m "Mark Menger preview design as implemented"
jj new
```
