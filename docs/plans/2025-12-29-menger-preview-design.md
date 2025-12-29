# Menger Sponge Preview Rendering

**Date**: 2025-12-29
**Status**: Design complete

## Problem

The current voxel preview uses translucent rendering, which makes internal geometry visible and creates visual glitches. The stepped internal structure of adjacent voxels shows through, making the preview look messy.

## Solution

Render each preview voxel as a **Menger sponge (iteration 1)** - a 3x3x3 grid of sub-voxels with the 7 axial positions removed. This creates a hollow structure that provides see-through visibility while using fully opaque rendering, eliminating all depth sorting issues.

### Visual Effect

- Each voxel becomes 20 sub-voxels at 1/3 scale
- Hollow center visible from any angle
- Two-tone shading: exterior faces at full brightness, interior faces at 60%
- Material colors preserved (copper, lead, gold, iron)

## Design

### Sub-voxel Pattern

For each voxel, generate a 3x3x3 grid of sub-voxels. Exclude the 7 axial positions:

- Center: `(1,1,1)`
- Face-centers: `(0,1,1)`, `(2,1,1)`, `(1,0,1)`, `(1,2,1)`, `(1,1,0)`, `(1,1,2)`

This leaves 20 sub-voxels: the 8 corners plus 12 edge-centers.

### Two-Tone Shading

Instead of per-face directional shading, use two colors:

- **Exterior faces**: 100% material color - faces on the outer shell
- **Interior faces**: 60% material color - faces adjacent to removed positions (facing into the hollow)

A face is interior if the neighboring sub-voxel position (in face direction) is one of the 7 removed axial positions.

### Rendering Changes

Switch from translucent to fully opaque rendering:

- Disable alpha blending (`GlToggleBlend(false)`)
- Alpha channel always 255 in vertex colors
- Standard opaque depth testing handles occlusion correctly

## Files to Modify

| File | Changes |
|------|---------|
| `VoxelPreviewMesh.cs` | Generate 20 sub-voxels per voxel in Menger pattern; two-tone shading (exterior 100%, interior 60%); alpha always 255 |
| `VoxelPreviewRenderer.cs` | Disable blending, render fully opaque |

## Implementation Notes

### Sub-voxel Generation

```csharp
// Removed positions (axial)
static readonly HashSet<(int, int, int)> RemovedPositions = new() {
    (1,1,1), (0,1,1), (2,1,1), (1,0,1), (1,2,1), (1,1,0), (1,1,2)
};

// For each voxel, emit sub-voxels
for (int sx = 0; sx < 3; sx++)
for (int sy = 0; sy < 3; sy++)
for (int sz = 0; sz < 3; sz++) {
    if (RemovedPositions.Contains((sx, sy, sz))) continue;
    // Emit sub-voxel at 1/3 scale, offset by (sx, sy, sz) * (VoxelSize/3)
}
```

### Interior Face Detection

A face is interior if the adjacent sub-voxel position is in `RemovedPositions`:

```csharp
bool IsInteriorFace(int sx, int sy, int sz, int dx, int dy, int dz) {
    return RemovedPositions.Contains((sx + dx, sy + dy, sz + dz));
}
```

### Scale Factor

Sub-voxels are 1/3 the size of regular voxels:
- `SubVoxelSize = VoxelSize / 3`
- Position offset within parent voxel: `(sx, sy, sz) * SubVoxelSize`

## Not Included

- Config file (deferred - not needed for this feature)
- Animation (static geometry is simpler and sufficient)
- Custom shader (standard opaque rendering works)

## Testing

- Visual inspection in-game with various cable configurations
- Verify material colors are distinguishable
- Verify interior/exterior shading provides depth cues
- Check performance with large previews (should be fine - just more triangles)
