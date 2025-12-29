# Documentation Reorganization Design

## Goal

Move design documents from `context/` to `docs/`, organized by layer, and rewrite each to reflect actual implementation rather than original design intent.

## Target Structure

```
docs/
├── plans/              # (existing - implementation plans)
├── mna/
│   ├── theory.md       # MNA theory reference
│   ├── solver.md       # Low-level solver
│   └── api.md          # High-level API (merged)
├── voxel/
│   ├── storage.md      # VoxelGrid/SVO architecture
│   └── topology.md     # Topology extraction (new)
├── handbook/
│   ├── architecture.md # 2D game architecture
│   └── design.md       # Educational game design
└── mod/
    ├── integration.md  # VS mod integration
    └── cable-layer.md  # Wire tool feature
```

## Approach

Each doc is rewritten by a parallel agent (Opus) that:

1. Reads the existing doc as context for intent/structure
2. Explores the actual code in the relevant `src/` subdirectory
3. Writes a fresh doc describing what the code actually does
4. Follows consistent format (see below)

## Agent Assignments

| Agent | Source Docs | Target | Code to Explore |
|-------|-------------|--------|-----------------|
| 1 | mna-theory.md | docs/mna/theory.md | src/mna/solver/*.cs |
| 2 | mna-core-solver.md | docs/mna/solver.md | src/mna/solver/*.cs |
| 3 | mna-api.md + simulation-api.md | docs/mna/api.md | src/mna/api/*.cs |
| 4 | voxel-storage.md | docs/voxel/storage.md | src/voxel/*.cs |
| 5 | (new) | docs/voxel/topology.md | src/mna/topology/*.cs |
| 6 | 2d-game-architecture.md | docs/handbook/architecture.md | src/handbook/*.cs |
| 7 | 2d-game-design.md | docs/handbook/design.md | src/handbook/*.cs |
| 8 | vsintegration.md + cable-layer.md | docs/mod/integration.md + cable-layer.md | src/mod/**/*.cs |

## Document Format

```markdown
# Title

Brief overview (2-3 sentences) of what this component does.

## Key Files

src/layer/
├── File.cs        # Brief description
└── Other.cs       # Brief description

## Architecture

How the pieces fit together - data flow, responsibilities, key abstractions.

## [Component-specific sections]

- For solver: algorithms, matrix assembly, component stamps
- For API: public interface, usage patterns, ID types
- For voxel: data structures, algorithms
- For mod: VS integration points, network protocol

## Usage Examples (where applicable)

Brief code snippets showing typical usage.
```

No "last updated" timestamps - docs describe the current state as of the commit they're in.

## Cleanup

After all docs are written:
- Delete `context/` directory
- Update `AGENTS.md` to reference `docs/` instead of `context/`
- Commit changes
